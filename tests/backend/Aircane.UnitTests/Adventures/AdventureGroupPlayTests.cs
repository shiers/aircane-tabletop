using System.Security.Claims;
using Aircane.Api.Controllers;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Ai;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Aircane.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>
/// Group play: several participants of a play session take turns in an adventure run, the host
/// passes the turn, and the AI is told who is playing. Runs against the in-memory provider with the
/// real service and controller; solo behaviour is checked alongside so it is shown to be unchanged.
/// </summary>
public sealed class AdventureGroupPlayTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly RecordingSessionHubNotifier _hub = new();
    private readonly CountingPlayerActionService _playerActions = new();
    private readonly AdventureSessionService _sut;

    private Campaign _campaign = null!;
    private GeneratedAdventure _adventure = null!;
    private Session _playSession = null!;
    private SessionParticipant _alice = null!;
    private SessionParticipant _bob = null!;
    private SessionParticipant _cara = null!;

    private readonly string _dbName = Guid.NewGuid().ToString();

    public AdventureGroupPlayTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(_dbName).Options);
        _sut = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance,
            _hub);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>A campaign with an approved adventure and a play session of three approved players.</summary>
    private async Task SeedAsync(GeneratedAdventureStatus status = GeneratedAdventureStatus.Approved)
    {
        _campaign = new Campaign("Test Campaign", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _adventure = AdventureSessionTestData.CreateAdventure(status);
        _playSession = new Session(_campaign.Id, "Table", SessionAccessMode.LocalLan, "hash", SessionStatus.Active);
        _alice = new SessionParticipant(_playSession.Id, "Alice", ParticipantRole.Player, isApproved: true);
        _bob = new SessionParticipant(_playSession.Id, "Bob", ParticipantRole.Player, isApproved: true);
        _cara = new SessionParticipant(_playSession.Id, "Cara", ParticipantRole.Player, isApproved: true);

        _db.Campaigns.Add(_campaign);
        _db.GeneratedAdventures.Add(_adventure);
        _db.Sessions.Add(_playSession);
        _db.SessionParticipants.AddRange(_alice, _bob, _cara);
        await _db.SaveChangesAsync();
    }

    /// <summary>Alice, Bob, Cara in that order, Alice up first.</summary>
    private async Task<AdventureSession> StartGroupAsync()
    {
        await SeedAsync();
        return await _sut.StartGroupSessionAsync(
            _adventure.Id, _campaign.Id, [_alice.Id, _bob.Id, _cara.Id]);
    }

    private Task<AdventureSession> ReloadAsync(Guid id) =>
        _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == id);

    private AdventureSessionsController CreateController(ClaimsPrincipal? user)
        => new(_sut, _playerActions, sessionHosting: new DbBackedSessionHosting(_db), NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = LocalHostHttpContext.Create(user),
            },
        };

    private static ClaimsPrincipal Host() => Token("Host", Guid.NewGuid(), Guid.NewGuid());

    private ClaimsPrincipal Player(SessionParticipant participant) => Token("Player", _playSession.Id, participant.Id);

    private static ClaimsPrincipal Token(string role, Guid sessionId, Guid participantId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Role, role),
        new Claim("sid", sessionId.ToString()),
        new Claim("pid", participantId.ToString()),
    ], authenticationType: "Test"));

    // ── Starting a group run ──────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_StartGroup_SetsIsGroupAndTurnOrder()
    {
        var run = await StartGroupAsync();

        var saved = await ReloadAsync(run.Id);
        Assert.True(saved.IsGroup);
        Assert.Equal([_alice.Id, _bob.Id, _cara.Id], saved.TurnOrder);
        Assert.Equal(1, saved.CurrentRound);
        Assert.Equal(AdventureSessionStatus.InProgress, saved.Status);
        // The run lives in the table's play session, so its players can reach it and hear its events.
        Assert.Equal(_playSession.Id, saved.SessionId);
    }

    [Fact]
    public async Task AdventureSession_StartGroup_SetsFirstParticipantAsActive()
    {
        var run = await StartGroupAsync();

        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_alice.Id, saved.ActiveParticipantId);

        var state = await _sut.GetSessionStateAsync(run.Id);
        Assert.True(state.IsGroup);
        Assert.Equal(_alice.Id, state.ActiveParticipantId);
        Assert.Equal("Alice", state.ActiveParticipantName);
        Assert.Equal(
            [("Alice", true), ("Bob", false), ("Cara", false)],
            state.TurnOrder.Select(t => (t.ParticipantName, t.IsActive)));
    }

    [Fact]
    public async Task AdventureSession_StartGroup_Idempotent_ReturnsExisting()
    {
        var first = await StartGroupAsync();

        // Even a different list of players resumes the running group rather than reshuffling it.
        var second = await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_cara.Id]);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.AdventureSessions.CountAsync());
        Assert.Equal([_alice.Id, _bob.Id, _cara.Id], (await ReloadAsync(first.Id)).TurnOrder);
    }

    [Fact]
    public async Task AdventureSession_StartGroup_DifferentAdventureInProgress_Throws()
    {
        await StartGroupAsync();
        var other = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(other);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(other.Id, _campaign.Id, [_alice.Id]));
    }

    [Fact]
    public async Task AdventureSession_StartGroup_DraftAdventure_Throws()
    {
        await SeedAsync(GeneratedAdventureStatus.Draft);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id]));
    }

    [Fact]
    public async Task AdventureSession_StartGroup_ParticipantNotInCurrentSession_Throws()
    {
        await SeedAsync();
        var stranger = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, stranger]));

        Assert.Contains(stranger.ToString(), ex.Message);
        Assert.Equal(0, await _db.AdventureSessions.CountAsync());
    }

    [Fact]
    public async Task AdventureSession_StartGroup_UnapprovedOrSpectator_Throws()
    {
        await SeedAsync();
        var pending = new SessionParticipant(_playSession.Id, "Pending", ParticipantRole.Player, isApproved: false);
        var watcher = new SessionParticipant(_playSession.Id, "Watcher", ParticipantRole.Spectator, isApproved: true);
        _db.SessionParticipants.AddRange(pending, watcher);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, pending.Id]));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, watcher.Id]));
    }

    [Fact]
    public async Task AdventureSession_StartGroup_ParticipantOfAnotherSession_Throws()
    {
        await SeedAsync();
        var elsewhere = new Session(_campaign.Id, "Old table", SessionAccessMode.LocalLan, "hash", SessionStatus.Ended);
        var outsider = new SessionParticipant(elsewhere.Id, "Outsider", ParticipantRole.Player, isApproved: true);
        _db.Sessions.Add(elsewhere);
        _db.SessionParticipants.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [outsider.Id]));
    }

    [Fact]
    public async Task AdventureSession_StartGroup_NoParticipantsOrDuplicates_Throws()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, _alice.Id]));
    }

    [Fact]
    public async Task AdventureSession_StartGroup_SoloRunOfSameAdventureInProgress_Throws()
    {
        await SeedAsync();
        await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id]));
    }

    [Fact]
    public async Task AdventureSession_StartSolo_GroupRunInProgress_Throws()
    {
        // A solo "start" must not hand back a group's run as if it were the caller's own.
        await StartGroupAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null));
        Assert.Contains("start-group", ex.Message);
    }

    // ── Turn gating on player-action ──────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_PlayerAction_WrongTurn_Returns403()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Player(_bob))
            .SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I charge in"), default);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(forbidden.Value);
        Assert.Equal("It is not your turn. Waiting for Alice.", problem.Detail);
        Assert.Equal(0, _playerActions.Calls);   // the AI is never reached
    }

    [Fact]
    public async Task AdventureSession_PlayerAction_NoParticipantIdentity_IsNotTheirTurn()
    {
        var run = await StartGroupAsync();

        // The Host's token names a participant who is not in the turn order, and an unauthenticated
        // local caller names none: neither is the active participant, so neither can act for them.
        foreach (var caller in new ClaimsPrincipal?[] { Host(), null })
        {
            var result = await CreateController(caller)
                .SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I act"), default);

            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        Assert.Equal(0, _playerActions.Calls);
    }

    [Fact]
    public async Task AdventureSession_PlayerAction_CorrectTurn_Succeeds()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Player(_alice))
            .SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I light a torch"), default);

        var body = Assert.IsType<AdventureSessionActionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("The torch flares.", body.Narration);
        Assert.Null(body.State);   // a Player is never sent the full run state
        Assert.Null(body.PrivateDmNote);
        Assert.Empty(body.Actions);
        Assert.Equal(1, _playerActions.Calls);
    }

    [Fact]
    public async Task AdventureSession_PlayerAction_Solo_SkipsTurnGating()
    {
        await SeedAsync();
        var solo = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);
        await _sut.AttachPlaySessionAsync(solo.Id, _playSession.Id);

        // A solo run has no turn order, so any participant of its session may act.
        var result = await CreateController(Player(_bob))
            .SubmitPlayerAction(solo.Id, new AdventureSessionActionRequest("I look around"), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, _playerActions.Calls);
    }

    // ── Advancing the turn ────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_AdvanceTurn_MovesToNextParticipant()
    {
        var run = await StartGroupAsync();

        await _sut.AdvanceTurnAsync(run.Id);

        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_bob.Id, saved.ActiveParticipantId);
        Assert.Equal(1, saved.CurrentRound);

        await _sut.AdvanceTurnAsync(run.Id);
        saved = await ReloadAsync(run.Id);
        Assert.Equal(_cara.Id, saved.ActiveParticipantId);
        Assert.Equal(1, saved.CurrentRound);
    }

    [Fact]
    public async Task AdventureSession_AdvanceTurn_WrapsAround_IncrementsRound()
    {
        var run = await StartGroupAsync();

        await _sut.AdvanceTurnAsync(run.Id);   // Bob
        await _sut.AdvanceTurnAsync(run.Id);   // Cara
        await _sut.AdvanceTurnAsync(run.Id);   // back to Alice: round 2

        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_alice.Id, saved.ActiveParticipantId);
        Assert.Equal(2, saved.CurrentRound);
    }

    [Fact]
    public async Task AdventureSession_AdvanceTurn_SinglePlayer_StartsANewRoundEachTime()
    {
        await SeedAsync();
        var run = await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id]);

        await _sut.AdvanceTurnAsync(run.Id);

        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_alice.Id, saved.ActiveParticipantId);
        Assert.Equal(2, saved.CurrentRound);
    }

    [Fact]
    public async Task AdventureSession_AdvanceTurn_BroadcastsTurnAdvancedEvent()
    {
        var run = await StartGroupAsync();

        await _sut.AdvanceTurnAsync(run.Id);

        var (group, evt) = Assert.Single(_hub.TurnsAdvanced);
        Assert.Equal(_playSession.Id, group);
        Assert.Equal(run.Id, evt.SessionId);
        Assert.Equal(_alice.Id, evt.PreviousParticipantId);
        Assert.Equal(_bob.Id, evt.ActiveParticipantId);
        Assert.Equal("Bob", evt.ActiveParticipantName);
        Assert.Equal(1, evt.CurrentRound);
    }

    [Fact]
    public async Task AdventureSession_AdvanceTurn_OnSoloRun_Throws()
    {
        await SeedAsync();
        var solo = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AdvanceTurnAsync(solo.Id));
    }

    [Fact]
    public async Task AdventureSession_AdvanceTurn_AfterAbandon_Throws()
    {
        var run = await StartGroupAsync();
        await _sut.AbandonAdventureAsync(run.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AdvanceTurnAsync(run.Id));
    }

    // ── Adding participants ───────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_AddParticipant_AppendsTurnOrder()
    {
        await SeedAsync();
        var run = await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, _bob.Id]);

        await _sut.AddParticipantAsync(run.Id, _cara.Id);

        var saved = await ReloadAsync(run.Id);
        Assert.Equal([_alice.Id, _bob.Id, _cara.Id], saved.TurnOrder);
        Assert.Equal(_alice.Id, saved.ActiveParticipantId);   // joining does not steal the turn

        var (group, evt) = Assert.Single(_hub.ParticipantsJoinedAdventure);
        Assert.Equal(_playSession.Id, group);
        Assert.Equal(run.Id, evt.SessionId);
        Assert.Equal(_cara.Id, evt.ParticipantId);
        Assert.Equal("Cara", evt.ParticipantName);
        Assert.Equal(2, evt.TurnPosition);   // zero-based: third in the order
    }

    [Fact]
    public async Task AdventureSession_AddParticipant_AlreadyInRun_Throws()
    {
        var run = await StartGroupAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AddParticipantAsync(run.Id, _bob.Id));
        Assert.Empty(_hub.ParticipantsJoinedAdventure);
    }

    [Fact]
    public async Task AdventureSession_AddParticipant_NotInPlaySession_Throws()
    {
        await SeedAsync();
        var run = await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AddParticipantAsync(run.Id, Guid.NewGuid()));
        Assert.Equal([_alice.Id], (await ReloadAsync(run.Id)).TurnOrder);
    }

    // ── AI prompt ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_GroupContext_InjectedInPrompt_WhenIsGroup()
    {
        var run = await StartGroupAsync();
        await _sut.AdvanceTurnAsync(run.Id);   // Bob is up

        var context = await _sut.BuildActiveAdventureContextAsync(_campaign.Id);

        Assert.NotNull(context);
        Assert.Contains("GROUP PLAY — 3 players are in this session.", context.Text);
        Assert.Contains("Turn order: Alice, Bob, Cara.", context.Text);
        Assert.Contains("Currently acting: Bob.", context.Text);
        Assert.Contains("Round: 1.", context.Text);
        Assert.True(context.IsGroup);
        // ...alongside, not instead of, the usual adventure context.
        Assert.Contains("Current Adventure: The Sunken Archive", context.Text);
        Assert.Contains("The Sunken Library", context.Text);
    }

    // How to run a table of players lives in the group instruction block, not in the context.
    [Fact]
    public async Task AdventureSession_GroupContext_CarriesFactsOnly_NoInstructions()
    {
        await StartGroupAsync();

        var text = (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!.Text;

        Assert.DoesNotContain("Address narration", text);
        Assert.DoesNotContain("End each narration", text);
    }

    [Fact]
    public async Task AdventureSession_GroupContext_NamesEachPlayersCharacter()
    {
        await SeedAsync();
        var mira = new Character("Mira", "D&D 5e", "2014", 3, "{}", "{}", _campaign.Id);
        var thorn = new Character("Thorn", "D&D 5e", "2014", 3, "{}", "{}", _campaign.Id);
        _db.Characters.AddRange(mira, thorn);
        await _db.SaveChangesAsync();
        // Participants' characters are assigned after they join.
        await AssignCharacterAsync(_alice.Id, mira.Id);
        await AssignCharacterAsync(_bob.Id, thorn.Id);
        var run = await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, _bob.Id, _cara.Id]);
        await _sut.AdvanceTurnAsync(run.Id);   // Bob (Thorn) is up

        var text = (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!.Text;

        // Cara has no character in this app, so is listed by the player's own name.
        Assert.Contains("Turn order: Mira (played by Alice), Thorn (played by Bob), Cara.", text);
        Assert.Contains("Currently acting: Thorn (played by Bob).", text);
    }

    private async Task AssignCharacterAsync(Guid participantId, Guid characterId)
    {
        var participant = await _db.SessionParticipants.SingleAsync(p => p.Id == participantId);
        participant.CharacterId = characterId;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdventureSession_GroupContext_AbsentFromPrompt_WhenSolo()
    {
        await SeedAsync();
        await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        var context = await _sut.BuildActiveAdventureContextAsync(_campaign.Id);

        Assert.NotNull(context);
        Assert.False(context.IsGroup);
        Assert.DoesNotContain("GROUP PLAY", context.Text);
        Assert.DoesNotContain("Turn order", context.Text);
        Assert.Contains("Current Adventure: The Sunken Archive", context.Text);
    }

    [Fact]
    public async Task AdventureSession_GroupContext_WithOnePlayer_SaysOnePlayer()
    {
        await SeedAsync();
        await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id]);

        var text = (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!.Text;

        Assert.Contains("GROUP PLAY — 1 player is in this session.", text);
        Assert.DoesNotContain("1 players", text);
        Assert.Contains("Turn order: Alice.", text);
        Assert.Contains("Currently acting: Alice.", text);
    }

    [Fact]
    public async Task AdventureSession_GroupContext_WithNobodyLeft_KeepsTheBlock_AndSaysNoOneIsActing()
    {
        var run = await StartGroupAsync();
        var tracked = await _db.AdventureSessions.SingleAsync(r => r.Id == run.Id);
        tracked.TurnOrder = [];
        tracked.ActiveParticipantId = null;
        await _db.SaveChangesAsync();

        var context = (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!;

        // The group instructions refer to the GROUP PLAY block, so it is there even with no one in it.
        Assert.True(context.IsGroup);
        Assert.Contains("GROUP PLAY — no players are in the turn order right now, so no one is acting.", context.Text);
        Assert.Contains("Wait for the host to add a player.", context.Text);
        Assert.DoesNotContain("Currently acting:", context.Text);
        Assert.DoesNotContain("Turn order:", context.Text);
    }

    [Fact]
    public async Task AdventureSession_GroupContext_FollowsThePendingRollBlock()
    {
        var run = await StartGroupAsync();
        await _sut.UpdatePendingRollAsync(run.Id, "Dexterity (Stealth)", 14);

        var text = (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!.Text;

        // The pending roll stays the first, unmissable thing in the context.
        Assert.StartsWith("PENDING ROLL:", text);
        Assert.True(text.IndexOf("GROUP PLAY", StringComparison.Ordinal) > text.IndexOf("PENDING ROLL", StringComparison.Ordinal));
        // ...and says who owes it, not "the player".
        Assert.StartsWith("PENDING ROLL: Earlier this turn, Alice was asked to roll Dexterity (Stealth), DC 14.", text);
        Assert.DoesNotContain("The player was previously asked", text);
    }

    [Fact]
    public async Task AdventureSession_AdvanceTurn_ClearsThePendingRoll()
    {
        var run = await StartGroupAsync();
        await _sut.UpdatePendingRollAsync(run.Id, "Dexterity (Stealth)", 14);

        await _sut.AdvanceTurnAsync(run.Id);   // Bob is up

        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_bob.Id, saved.ActiveParticipantId);
        Assert.Null(saved.PendingRollLabel);
        Assert.Null(saved.PendingRollDC);
        Assert.DoesNotContain("PENDING ROLL", (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!.Text);
    }

    [Fact]
    public async Task AdventureSession_GroupContext_ListsPlayersWithoutCharacter()
    {
        await SeedAsync();
        var mira = new Character("Mira", "D&D 5e", "2014", 3, "{}", "{}", _campaign.Id);
        _db.Characters.Add(mira);
        await _db.SaveChangesAsync();
        await AssignCharacterAsync(_alice.Id, mira.Id);
        await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, _bob.Id, _cara.Id]);

        var context = (await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!;

        Assert.Equal(["Bob", "Cara"], context.PlayersWithoutCharacter);
    }

    [Fact]
    public async Task AdventureSession_SoloContext_HasNoPlayersWithoutCharacterList()
    {
        await SeedAsync();
        await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        Assert.Null((await _sut.BuildActiveAdventureContextAsync(_campaign.Id))!.PlayersWithoutCharacter);
    }

    // ── Controller ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_StartGroup_Returns201()
    {
        await SeedAsync();

        var result = await CreateController(Host()).StartGroupSession(
            new StartGroupAdventureSessionRequest(_adventure.Id, _campaign.Id, [_alice.Id, _bob.Id]), default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var state = Assert.IsType<AdventureSessionStateDto>(created.Value);
        Assert.True(state.IsGroup);
        Assert.Equal(_alice.Id, state.ActiveParticipantId);
        Assert.Equal(2, state.TurnOrder.Count);
    }

    [Fact]
    public async Task AdventureSessionsController_StartGroup_AgainForSameAdventure_Returns200WithExistingRun()
    {
        await SeedAsync();
        var controller = CreateController(Host());
        var request = new StartGroupAdventureSessionRequest(_adventure.Id, _campaign.Id, [_alice.Id, _bob.Id]);
        var first = Assert.IsType<AdventureSessionStateDto>(
            Assert.IsType<CreatedAtActionResult>(await controller.StartGroupSession(request, default)).Value);

        var again = await controller.StartGroupSession(request, default);

        var state = Assert.IsType<AdventureSessionStateDto>(Assert.IsType<OkObjectResult>(again).Value);
        Assert.Equal(first.Id, state.Id);
    }

    [Fact]
    public async Task AdventureSessionsController_StartGroup_DifferentAdventureInProgress_Returns409()
    {
        await StartGroupAsync();
        var other = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(other);
        await _db.SaveChangesAsync();

        var result = await CreateController(Host()).StartGroupSession(
            new StartGroupAdventureSessionRequest(other.Id, _campaign.Id, [_alice.Id]), default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task AdventureSessionsController_StartGroup_Player_IsForbidden()
    {
        await SeedAsync();

        var result = await CreateController(Player(_alice)).StartGroupSession(
            new StartGroupAdventureSessionRequest(_adventure.Id, _campaign.Id, [_alice.Id]), default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, await _db.AdventureSessions.CountAsync());
    }

    [Fact]
    public async Task AdventureSessionsController_StartGroup_ParticipantNotInSession_Returns400()
    {
        await SeedAsync();

        var result = await CreateController(Host()).StartGroupSession(
            new StartGroupAdventureSessionRequest(_adventure.Id, _campaign.Id, [Guid.NewGuid()]), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AdventureSessionsController_AdvanceTurn_RequiresHostRole()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Host()).AdvanceTurn(run.Id, null, default);

        var state = Assert.IsType<AdventureSessionStateDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(_bob.Id, state.ActiveParticipantId);
        Assert.Equal("Bob", state.ActiveParticipantName);
        Assert.Equal(1, state.CurrentRound);
    }

    // The acting Player may end their own turn (see AdventureTurnEndTests); a human DM or a spectator may not
    // pass anyone's, even when their token carries the acting participant's id.
    [Theory]
    [InlineData("HumanDm")]
    [InlineData("Spectator")]
    public async Task AdventureSessionsController_AdvanceTurn_Returns403ForOtherRoles(string role)
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Token(role, _playSession.Id, _alice.Id)).AdvanceTurn(run.Id, null, default);

        Assert.IsType<ForbidResult>(result);   // rendered as 403
        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Empty(_hub.TurnsAdvanced);
    }

    [Fact]
    public async Task AdventureSessionsController_AddParticipant_AppendsAndReturnsState()
    {
        await SeedAsync();
        var run = await _sut.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id]);

        var result = await CreateController(Host()).AddParticipant(run.Id, new AddAdventureParticipantRequest(_bob.Id), default);

        var state = Assert.IsType<AdventureSessionStateDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(["Alice", "Bob"], state.TurnOrder.Select(t => t.ParticipantName));
    }

    [Fact]
    public async Task AdventureSessionsController_AddParticipant_AlreadyInRun_Returns409()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Host()).AddParticipant(run.Id, new AddAdventureParticipantRequest(_bob.Id), default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task AdventureSessionsController_AddParticipant_Player_IsForbidden()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Player(_alice)).AddParticipant(
            run.Id, new AddAdventureParticipantRequest(Guid.NewGuid()), default);

        Assert.IsType<ForbidResult>(result);
    }

    // ── Abandon / advance-scene: Host only in a group run ─────────────────────

    private const string NextScene = "scene-2";

    private async Task<(Guid RunId, ClaimsPrincipal Player)> StartSoloRunWithPlayerAsync()
    {
        await SeedAsync();
        var solo = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);
        await _sut.AttachPlaySessionAsync(solo.Id, _playSession.Id);
        return (solo.Id, Player(_alice));
    }

    [Fact]
    public async Task AdventureSessionsController_Abandon_GroupRun_PlayerGets403()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Player(_alice)).Abandon(run.Id, default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(AdventureSessionStatus.InProgress, (await ReloadAsync(run.Id)).Status);
    }

    [Fact]
    public async Task AdventureSessionsController_Abandon_GroupRun_HostGets200()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Host()).Abandon(run.Id, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AdventureSessionStatus.Abandoned, (await ReloadAsync(run.Id)).Status);
    }

    [Fact]
    public async Task AdventureSessionsController_Abandon_SoloRun_PlayerGets200()
    {
        var (runId, player) = await StartSoloRunWithPlayerAsync();

        var result = await CreateController(player).Abandon(runId, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AdventureSessionStatus.Abandoned, (await ReloadAsync(runId)).Status);
    }

    [Fact]
    public async Task AdventureSessionsController_AdvanceScene_GroupRun_PlayerGets403()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Player(_alice))
            .AdvanceScene(run.Id, new AdvanceSceneRequest(NextScene), default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("scene-1", (await ReloadAsync(run.Id)).CurrentSceneId);
    }

    [Fact]
    public async Task AdventureSessionsController_AdvanceScene_GroupRun_HostGets200()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Host())
            .AdvanceScene(run.Id, new AdvanceSceneRequest(NextScene), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(NextScene, (await ReloadAsync(run.Id)).CurrentSceneId);
    }

    [Fact]
    public async Task AdventureSessionsController_AdvanceScene_SoloRun_PlayerGets200()
    {
        var (runId, player) = await StartSoloRunWithPlayerAsync();

        var result = await CreateController(player)
            .AdvanceScene(runId, new AdvanceSceneRequest(NextScene), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(NextScene, (await ReloadAsync(runId)).CurrentSceneId);
    }

    // ── Solo start against a group run ────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_StartSolo_GroupRunInProgress_Returns400PointingToStartGroup()
    {
        await StartGroupAsync();

        var result = await CreateController(Host()).StartSession(
            new StartAdventureSessionRequest(_adventure.Id, _campaign.Id), default);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Contains("start-group", problem.Detail);
    }

    // ── Solo state is unchanged ───────────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_Solo_HasNoGroupState()
    {
        await SeedAsync();
        var solo = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        var saved = await ReloadAsync(solo.Id);
        Assert.False(saved.IsGroup);
        Assert.Empty(saved.TurnOrder);
        Assert.Null(saved.ActiveParticipantId);
        Assert.Equal(0, saved.CurrentRound);

        var state = await _sut.GetSessionStateAsync(solo.Id);
        Assert.False(state.IsGroup);
        Assert.Null(state.ActiveParticipantId);
        Assert.Null(state.ActiveParticipantName);
        Assert.Empty(state.TurnOrder);
    }

    // ── Ending a turn: the acting player, the host, and a request that arrives late ─────────────

    private AdventureSessionsController CreateControllerWith(IPlayerActionService ai, ClaimsPrincipal? user)
        => new(_sut, ai, new DbBackedSessionHosting(_db), NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = LocalHostHttpContext.Create(user) },
        };

    private async Task<AdventureSession> StartGroupAsync(TurnAdvanceMode mode)
    {
        await SeedAsync();
        return await _sut.StartGroupSessionAsync(
            _adventure.Id, _campaign.Id, [_alice.Id, _bob.Id, _cara.Id], null, mode);
    }

    private async Task<SessionParticipant> AddWaitingParticipantAsync()
    {
        var dana = new SessionParticipant(_playSession.Id, "Dana", ParticipantRole.Player, isApproved: false);
        _db.SessionParticipants.Add(dana);
        await _db.SaveChangesAsync();
        return dana;
    }

    [Fact]
    public async Task EndTurn_ActingPlayer_MovesTheTurn_ClearsThePendingRoll_AndRecordsWhoEndedIt()
    {
        var run = await StartGroupAsync();
        await _sut.UpdatePendingRollAsync(run.Id, "Dexterity (Stealth)", 14);

        var result = await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);

        var view = Assert.IsType<PlayerAdventureRunDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(_bob.Id, view.ActiveParticipantId);
        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_bob.Id, saved.ActiveParticipantId);
        Assert.Null(saved.PendingRollLabel);
        Assert.Null(saved.PendingRollDC);

        // Broadcast: the existing adventure-state signal, and the turn event says a player ended it.
        Assert.Contains(_hub.StateChanges, c => c.Group == _playSession.Id && c.Event.Reason == "TurnAdvanced");
        Assert.Equal("Player", Assert.Single(_hub.TurnsAdvanced).Event.PassedBy);

        // Recorded: the event log has who ended it.
        var logged = await _db.CampaignEvents.AsNoTracking().SingleAsync(e => e.EventType == "Adventure:TurnAdvanced");
        Assert.Contains("\"passedBy\":\"Player\"", logged.PayloadJson);
        Assert.Contains(_alice.Id.ToString(), logged.PayloadJson);
    }

    [Fact]
    public async Task EndTurn_ThePlayerGetsTheTableView_NeverTheFullRunState()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);

        Assert.IsType<PlayerAdventureRunDto>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task EndTurn_NonActingPlayer_Gets403NotYourTurn_AndNothingMoves()
    {
        var run = await StartGroupAsync();   // Alice holds the turn

        var result = await CreateController(Player(_bob)).AdvanceTurn(run.Id, null, default);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(refused.Value);
        Assert.Equal("Not your turn", problem.Title);
        Assert.Contains("Waiting for Alice", problem.Detail);
        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Empty(_hub.TurnsAdvanced);
    }

    [Fact]
    public async Task EndTurn_ParticipantWaitingForApproval_IsRefusedWithTheirOwnReason()
    {
        var run = await StartGroupAsync();
        var dana = await AddWaitingParticipantAsync();

        var result = await CreateController(Player(dana)).AdvanceTurn(run.Id, null, default);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("Waiting for approval", Assert.IsType<ProblemDetails>(refused.Value).Title);
    }

    [Fact]
    public async Task EndTurn_DeniedActingPlayer_IsRefusedWithTheParticipantDeniedCode()
    {
        var run = await StartGroupAsync();
        _alice.IsApproved = false;
        _alice.IsDenied = true;
        await _db.SaveChangesAsync();

        var result = await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal("participant-denied", Assert.IsType<ProblemDetails>(refused.Value).Extensions["code"]);
        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
    }

    [Fact]
    public async Task EndTurn_ATokenForAnotherSession_IsForbidden()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Token("Player", Guid.NewGuid(), _alice.Id)).AdvanceTurn(run.Id, null, default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
    }

    [Fact]
    public async Task EndTurn_Host_MayPassAnyTurn_AndItIsRecordedAsTheHost()
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Host()).AdvanceTurn(run.Id, null, default);

        Assert.IsType<AdventureSessionStateDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Equal("Host", Assert.Single(_hub.TurnsAdvanced).Event.PassedBy);
    }

    // A request that arrives just after the turn already moved on ───────────────────────────

    [Fact]
    public async Task EndTurn_PlayersLateRequest_AfterTheHostPassed_IsA403_AndDoesNotMoveBobsTurn()
    {
        var run = await StartGroupAsync();
        await CreateController(Host()).AdvanceTurn(run.Id, null, default);   // Alice -> Bob

        // Alice's click arrives afterwards.
        var result = await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains("Waiting for Bob", Assert.IsType<ProblemDetails>(refused.Value).Detail);
        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);   // Bob still has it
        Assert.Single(_hub.TurnsAdvanced);
    }

    [Fact]
    public async Task EndTurn_PlayersDoubleClick_MovesTheTurnOnce()
    {
        var run = await StartGroupAsync();

        await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);
        var second = await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(second).StatusCode);
        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);   // not Cara
        Assert.Single(_hub.TurnsAdvanced);
    }

    [Fact]
    public async Task EndTurn_HostWhoseScreenWasStale_Gets409_AndNothingMoves()
    {
        var run = await StartGroupAsync();
        await CreateController(Player(_alice)).AdvanceTurn(run.Id, null, default);   // Alice ended her turn: Bob's

        // The host's screen still showed Alice as acting and asks to pass *her* turn.
        var result = await CreateController(Host()).AdvanceTurn(run.Id, _alice.Id, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("already passed to Bob", Assert.IsType<ProblemDetails>(conflict.Value).Detail);
        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Single(_hub.TurnsAdvanced);
    }

    [Fact]
    public async Task PassTurn_TwoRequestsForTheSameTurnAtOnce_MoveItExactlyOnce()
    {
        var run = await StartGroupAsync();
        // Two requests are two scopes, each with its own context over the same database. The in-memory provider
        // finishes each call before the next begins unless they really run in parallel, so the race is run on
        // pool threads, many times: without the lock some round would move the turn twice.
        var contexts = new List<AircaneDbContext>();
        AdventureSessionService NewService()
        {
            var ctx = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>().UseInMemoryDatabase(_dbName).Options);
            contexts.Add(ctx);
            return new AdventureSessionService(
                ctx,
                new AdventureRetrievalService(ctx, NullLogger<AdventureRetrievalService>.Instance),
                new CampaignStateService(ctx, NullLogger<CampaignStateService>.Instance),
                NullLogger<AdventureSessionService>.Instance,
                _hub);
        }
        var pass = new TurnPass(TurnPassedBy.Player, _alice.Id, _alice.Id);

        for (var round = 0; round < 40; round++)
        {
            // Back to Alice's turn.
            var tracked = await _db.AdventureSessions.SingleAsync(s => s.Id == run.Id);
            await _db.Entry(tracked).ReloadAsync();
            tracked.ActiveParticipantId = _alice.Id;
            await _db.SaveChangesAsync();
            _hub.TurnsAdvanced.Clear();

            var first = NewService();
            var second = NewService();
            using var start = new ManualResetEventSlim(false);
            var attempts = new[] { first, second }
                .Select(service => Task.Run(async () =>
                {
                    start.Wait();
                    try { await service.PassTurnAsync(run.Id, pass); return (Exception?)null; }
                    catch (Exception ex) { return ex; }
                }))
                .ToArray();
            start.Set();
            var results = await Task.WhenAll(attempts);

            Assert.True(results.Count(r => r is null) == 1, $"Round {round}: {results.Count(r => r is null)} requests moved the turn.");
            Assert.True(results.Count(r => r is TurnAlreadyMovedException) == 1, $"Round {round}: the late request was not told the turn had moved.");
            Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);   // once: Bob, not Cara
            Assert.Single(_hub.TurnsAdvanced);
        }

        contexts.ForEach(c => c.Dispose());
    }

    // ── Auto-advance ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StartGroup_DefaultsToManualOnly_AndTheStateSaysSo()
    {
        var run = await StartGroupAsync();

        Assert.Equal(TurnAdvanceMode.ManualOnly, (await ReloadAsync(run.Id)).TurnAdvance);
        Assert.Equal(TurnAdvanceMode.ManualOnly, (await _sut.GetSessionStateAsync(run.Id)).TurnAdvance);
    }

    [Fact]
    public async Task StartGroup_WithAfterEachAction_IsStored_AndShownInTheStateAndThePlayerView()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);

        Assert.Equal(TurnAdvanceMode.AfterEachAction, (await _sut.GetSessionStateAsync(run.Id)).TurnAdvance);
        var view = await _sut.GetPlayerRunViewAsync(_playSession.Id, _alice.Id);
        Assert.Equal(TurnAdvanceMode.AfterEachAction, view!.TurnAdvance);
    }

    [Fact]
    public async Task AutoAdvance_ManualOnly_NeverMovesTheTurn()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.ManualOnly);

        Assert.False(await _sut.AutoAdvanceAfterActionAsync(run.Id, _alice.Id));

        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Empty(_hub.TurnsAdvanced);
    }

    [Fact]
    public async Task AutoAdvance_AfterEachAction_MovesToTheNextPlayer_AndSaysItWasAutomatic()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);

        Assert.True(await _sut.AutoAdvanceAfterActionAsync(run.Id, _alice.Id));

        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Equal("Auto", Assert.Single(_hub.TurnsAdvanced).Event.PassedBy);
        Assert.Contains(_hub.StateChanges, c => c.Event.Reason == "TurnAdvanced");
    }

    [Fact]
    public async Task AutoAdvance_WhenTheAiAskedForARoll_StaysOnTheSamePlayer()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);
        await _sut.UpdatePendingRollAsync(run.Id, "Dexterity (Stealth)", 14);   // the AI is waiting for Alice's result

        Assert.False(await _sut.AutoAdvanceAfterActionAsync(run.Id, _alice.Id));

        var saved = await ReloadAsync(run.Id);
        Assert.Equal(_alice.Id, saved.ActiveParticipantId);
        Assert.Equal("Dexterity (Stealth)", saved.PendingRollLabel);   // not dropped
    }

    [Fact]
    public async Task AutoAdvance_WhenTheHostAlreadyPassedTheTurn_DoesNotSkipAnotherPlayer()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);
        await _sut.AdvanceTurnAsync(run.Id);   // the host passed Alice's turn while the AI was answering her

        Assert.False(await _sut.AutoAdvanceAfterActionAsync(run.Id, _alice.Id));

        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);   // Bob, not Cara
    }

    [Fact]
    public async Task AutoAdvance_WhenTheAiCompletedTheAdventure_DoesNothing()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);
        await _sut.CompleteAdventureAsync(run.Id, "The party won.");

        Assert.False(await _sut.AutoAdvanceAfterActionAsync(run.Id, _alice.Id));
    }

    [Fact]
    public async Task AutoAdvance_ThroughTheEndpoint_MovesOnAfterTheAiAnswered()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);

        var result = await CreateController(Player(_alice))
            .SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I light a torch"), default);

        var body = Assert.IsType<AdventureSessionActionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("The torch flares.", body.Narration);
        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Equal("Auto", Assert.Single(_hub.TurnsAdvanced).Event.PassedBy);
    }

    [Fact]
    public async Task AutoAdvance_ThroughTheEndpoint_ManualOnlyStaysPut()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.ManualOnly);

        await CreateController(Player(_alice))
            .SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I light a torch"), default);

        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
    }

    [Fact]
    public async Task AutoAdvance_WhenTheAiCallFails_TheTurnDoesNotMove()
    {
        var run = await StartGroupAsync(TurnAdvanceMode.AfterEachAction);
        var controller = CreateControllerWith(new ThrowingPlayerActionService(), Player(_alice));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            controller.SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I light a torch"), default));

        Assert.Equal(_alice.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
        Assert.Empty(_hub.TurnsAdvanced);

        // Alice can simply try again.
        var retry = await CreateController(Player(_alice))
            .SubmitPlayerAction(run.Id, new AdventureSessionActionRequest("I light a torch"), default);
        Assert.IsType<OkObjectResult>(retry);
        Assert.Equal(_bob.Id, (await ReloadAsync(run.Id)).ActiveParticipantId);
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class ThrowingPlayerActionService : IPlayerActionService
    {
        public Task<PlayerActionResponse> ProcessActionAsync(
            PlayerActionRequest request, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("The AI provider is unreachable.");
    }

    private sealed class CountingPlayerActionService : IPlayerActionService
    {
        public int Calls { get; private set; }

        public Task<PlayerActionResponse> ProcessActionAsync(
            PlayerActionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PlayerActionResponse(
                Narration: "The torch flares.",
                PrivateDmNote: null,
                ProposedActions: [],
                Citations: []));
        }
    }
}
