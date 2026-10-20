using System.Security.Claims;
using Aircane.Api.Controllers;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Sessions;
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
/// POST /api/sessions/{id}/ai/player-action knows nothing about whose turn it is, so while a group
/// adventure run is in progress it is closed (409) and the run is played through the adventure
/// endpoint, which enforces the acting player. Solo runs and campaigns with no run are unaffected.
/// </summary>
public sealed class PlayerActionControllerGroupGuardTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventureSessionService _adventures;
    private readonly RecordingPlayerActionService _playerActions = new();
    private readonly FakeSessionHostingService _hosting = new();

    private Campaign _campaign = null!;
    private GeneratedAdventure _adventure = null!;
    private Session _playSession = null!;
    private SessionParticipant _alice = null!;
    private SessionParticipant _bob = null!;

    public PlayerActionControllerGroupGuardTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _adventures = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        _campaign = new Campaign("Test Campaign", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _adventure = AdventureSessionTestData.CreateAdventure();
        _playSession = new Session(_campaign.Id, "Table", SessionAccessMode.LocalLan, "hash", SessionStatus.Active);
        _alice = new SessionParticipant(_playSession.Id, "Alice", ParticipantRole.Player, isApproved: true);
        _bob = new SessionParticipant(_playSession.Id, "Bob", ParticipantRole.Player, isApproved: true);

        _db.Campaigns.Add(_campaign);
        _db.GeneratedAdventures.Add(_adventure);
        _db.Sessions.Add(_playSession);
        _db.SessionParticipants.AddRange(_alice, _bob);
        await _db.SaveChangesAsync();

        _hosting.Register(_playSession);
        _hosting.Register(_alice);
        _hosting.Register(_bob);
    }

    /// <summary>A group run, Alice up first.</summary>
    private async Task<AdventureSession> StartGroupAsync()
    {
        await SeedAsync();
        return await _adventures.StartGroupSessionAsync(_adventure.Id, _campaign.Id, [_alice.Id, _bob.Id]);
    }

    private PlayerActionController CreateController(ClaimsPrincipal? user)
        => new(_playerActions, _hosting, _adventures, NullLogger<PlayerActionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = LocalHostHttpContext.Create(user),
            },
        };

    private ClaimsPrincipal Player(SessionParticipant participant) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Role, "Player"),
        new Claim("sid", _playSession.Id.ToString()),
        new Claim("pid", participant.Id.ToString()),
    ], authenticationType: "Test"));

    private Task<IActionResult> SubmitAsync(ClaimsPrincipal? user, Guid? sessionId = null, string text = "I search the shelves")
        => CreateController(user).SubmitPlayerAction(
            sessionId ?? _playSession.Id, new PlayerActionBody(Guid.NewGuid(), text), default);

    private static ProblemDetails AssertConflict(IActionResult result)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        return problem;
    }

    // ── Group run: closed to everyone ─────────────────────────────────────────

    [Fact]
    public async Task GenericPlayerAction_GroupRun_NonActingParticipant_IsRejected()
    {
        var run = await StartGroupAsync();

        var problem = AssertConflict(await SubmitAsync(Player(_bob)));   // Alice holds the turn

        Assert.Contains($"/api/adventure-sessions/{run.Id}/player-action", problem.Detail);
        Assert.Equal(0, _playerActions.Calls);
    }

    // Option (a): the endpoint is closed, not turn-aware, so the acting player is pointed to the
    // adventure endpoint too rather than being let through a path that skips its other behaviour.
    [Fact]
    public async Task GenericPlayerAction_GroupRun_ActingPlayer_IsPointedToTheAdventureEndpoint()
    {
        var run = await StartGroupAsync();

        var problem = AssertConflict(await SubmitAsync(Player(_alice)));

        Assert.Equal("Group adventure in progress", problem.Title);
        Assert.Contains($"POST /api/adventure-sessions/{run.Id}/player-action", problem.Detail);
        Assert.Contains("only the acting player can act", problem.Detail);
        Assert.Equal(0, _playerActions.Calls);
    }

    [Fact]
    public async Task GenericPlayerAction_GroupRun_Host_IsRejectedToo()
    {
        await StartGroupAsync();

        AssertConflict(await SubmitAsync(user: null));   // no participant identity: the trusted local host

        Assert.Equal(0, _playerActions.Calls);
    }

    [Fact]
    public async Task GenericPlayerAction_GroupRun_PendingRollIsLeftAlone()
    {
        var run = await StartGroupAsync();
        await _adventures.UpdatePendingRollAsync(run.Id, "Dexterity (Stealth)", 14);

        AssertConflict(await SubmitAsync(Player(_bob)));

        var saved = await _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);
        Assert.Equal("Dexterity (Stealth)", saved.PendingRollLabel);
        Assert.Equal(14, saved.PendingRollDC);
    }

    // The AI picks up the campaign's run whichever session id the request names.
    [Fact]
    public async Task GenericPlayerAction_GroupRun_AnotherSessionOfTheCampaign_IsRejectedToo()
    {
        await StartGroupAsync();
        var other = new Session(_campaign.Id, "Side table", SessionAccessMode.LocalLan, "hash", SessionStatus.Active);
        _db.Sessions.Add(other);
        await _db.SaveChangesAsync();
        _hosting.Register(other);

        AssertConflict(await SubmitAsync(user: null, sessionId: other.Id));

        Assert.Equal(0, _playerActions.Calls);
    }

    // ── Unchanged: solo run, no run, finished run ─────────────────────────────

    [Fact]
    public async Task GenericPlayerAction_SoloRun_BehavesAsBefore()
    {
        await SeedAsync();
        await _adventures.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        var result = await SubmitAsync(Player(_bob), text: "  I search the shelves  ");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, _playerActions.Calls);
        var request = _playerActions.LastRequest!;
        Assert.Equal(_playSession.Id, request.SessionId);
        Assert.Equal(_campaign.Id, request.CampaignId);
        Assert.Equal("I search the shelves", request.ActionText);
    }

    [Fact]
    public async Task GenericPlayerAction_NoRun_BehavesAsBefore()
    {
        await SeedAsync();

        var result = await SubmitAsync(Player(_alice));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, _playerActions.Calls);
    }

    [Fact]
    public async Task GenericPlayerAction_FinishedGroupRun_BehavesAsBefore()
    {
        var run = await StartGroupAsync();
        await _adventures.AbandonAdventureAsync(run.Id);

        var result = await SubmitAsync(Player(_bob));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, _playerActions.Calls);
    }

    [Fact]
    public async Task GenericPlayerAction_UnknownSession_Is404_BeforeAnyRunLookup()
    {
        await StartGroupAsync();

        var result = await SubmitAsync(Player(_bob), sessionId: Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, _playerActions.Calls);
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class RecordingPlayerActionService : IPlayerActionService
    {
        public int Calls { get; private set; }
        public PlayerActionRequest? LastRequest { get; private set; }

        public Task<PlayerActionResponse> ProcessActionAsync(
            PlayerActionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new PlayerActionResponse(
                Narration: "The torch flares.", PrivateDmNote: null, ProposedActions: [], Citations: []));
        }
    }

    /// <summary>Only the session and participant reads are used by the controller under test.</summary>
    private sealed class FakeSessionHostingService : ISessionHostingService
    {
        private readonly Dictionary<Guid, SessionDto> _sessions = [];

        public void Register(Session s) => _sessions[s.Id] = new SessionDto(
            s.Id, s.CampaignId, s.Name, s.AccessMode, s.Status, StartedAt: null, EndedAt: null, s.CreatedAt);

        private readonly Dictionary<Guid, ParticipantDto> _participants = [];

        public void Register(SessionParticipant p) => _participants[p.Id] = new ParticipantDto(
            p.Id, p.SessionId, p.DisplayName, p.Role, p.CharacterId, p.IsApproved, p.JoinedAt, p.LastSeenAt, p.IsDenied);

        public Task<ParticipantDto?> GetParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken = default)
            => Task.FromResult(_participants.GetValueOrDefault(participantId) is { } dto && dto.SessionId == sessionId ? dto : null);

        public Task<SessionDto?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_sessions.GetValueOrDefault(sessionId));

        public Task<(SessionDto Session, string InviteCode)> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JoinSessionResult> JoinSessionAsync(JoinSessionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ParticipantDto> ApproveParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ParticipantDto> DenyParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RejoinAttempt> RejoinAsync(Guid sessionId, string? secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RejoinStatusAttempt> GetRejoinStatusAsync(Guid sessionId, string? secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReplaceParticipantResult> ReplaceParticipantAsync(Guid sessionId, Guid oldParticipantId, Guid newParticipantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ParticipantDto> AssignCharacterAsync(Guid sessionId, Guid participantId, Guid characterId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ParticipantDto>> GetParticipantsAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EndSessionResponse> EndSessionAsync(Guid sessionId, string? summary, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PauseAiAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResumeAiResponse> ResumeAiAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
