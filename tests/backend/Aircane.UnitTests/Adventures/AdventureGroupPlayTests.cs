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

    public AdventureGroupPlayTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
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
        => new(_sut, _playerActions, sessionHosting: null!, NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) },
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
        Assert.Equal(_alice.Id, body.State.ActiveParticipantId);
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
        Assert.Contains("Address narration to the whole group.", context.Text);
        Assert.Contains("End each narration by asking Bob what they do, or posing a clear choice to them.", context.Text);
        // ...alongside, not instead of, the usual adventure context.
        Assert.Contains("Current Adventure: The Sunken Archive", context.Text);
        Assert.Contains("The Sunken Library", context.Text);
    }

    [Fact]
    public async Task AdventureSession_GroupContext_AbsentFromPrompt_WhenSolo()
    {
        await SeedAsync();
        await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        var context = await _sut.BuildActiveAdventureContextAsync(_campaign.Id);

        Assert.NotNull(context);
        Assert.DoesNotContain("GROUP PLAY", context.Text);
        Assert.DoesNotContain("Turn order", context.Text);
        Assert.Contains("Current Adventure: The Sunken Archive", context.Text);
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

        var result = await CreateController(Host()).AdvanceTurn(run.Id, default);

        var state = Assert.IsType<AdventureSessionStateDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(_bob.Id, state.ActiveParticipantId);
        Assert.Equal("Bob", state.ActiveParticipantName);
        Assert.Equal(1, state.CurrentRound);
    }

    [Theory]
    [InlineData("Player")]
    [InlineData("HumanDm")]
    [InlineData("Spectator")]
    public async Task AdventureSessionsController_AdvanceTurn_Returns403ForPlayer(string role)
    {
        var run = await StartGroupAsync();

        var result = await CreateController(Token(role, _playSession.Id, _alice.Id)).AdvanceTurn(run.Id, default);

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

    // ── Doubles ───────────────────────────────────────────────────────────────

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
