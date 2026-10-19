using System.Security.Claims;
using Aircane.Api.Controllers;
using Aircane.Application.DTOs.Adventures;
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
/// What a Player may do with a group run, endpoint by endpoint. At a table a Player reads the run through
/// <c>GET /api/sessions/{id}/adventure-run</c> and acts through <c>player-action</c>; everything else about
/// the run (its full state, history, recap, exits, resuming, moving or abandoning it) is the Host's.
/// Whatever they ask for, a participant who is waiting or was denied is told so.
/// </summary>
public sealed class AdventureGroupPlayerAccessTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventureSessionService _service;

    private Guid _runId;
    private Guid _campaignId;
    private Guid _playSessionId;
    private SessionParticipant _alice = null!;
    private SessionParticipant _waiting = null!;
    private SessionParticipant _denied = null!;

    public AdventureGroupPlayerAccessTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>A group run with Alice in the turn order; a waiting and a denied participant at the same table.</summary>
    private async Task SeedAsync()
    {
        var campaign = new Campaign("C", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        var adventure = AdventureSessionTestData.CreateAdventure();
        var session = new Session(campaign.Id, "Table", SessionAccessMode.LocalLan, "hash", SessionStatus.Active);
        _alice = new SessionParticipant(session.Id, "Alice", ParticipantRole.Player, isApproved: true);
        _waiting = new SessionParticipant(session.Id, "Wanda", ParticipantRole.Player, isApproved: false);
        _denied = new SessionParticipant(session.Id, "Mallory", ParticipantRole.Player, isApproved: false) { IsDenied = true };
        _db.Campaigns.Add(campaign);
        _db.GeneratedAdventures.Add(adventure);
        _db.Sessions.Add(session);
        _db.SessionParticipants.AddRange(_alice, _waiting, _denied);
        await _db.SaveChangesAsync();

        var run = await _service.StartGroupSessionAsync(adventure.Id, campaign.Id, [_alice.Id]);
        _runId = run.Id;
        _campaignId = campaign.Id;
        _playSessionId = session.Id;
    }

    private AdventureSessionsController CreateController(ClaimsPrincipal? user)
        => new(_service, new AllowAllPlayerActions(), new DbBackedSessionHosting(_db), NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = LocalHostHttpContext.Create(user) },
        };

    private ClaimsPrincipal Token(SessionParticipant who, string role = "Player") => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Role, role),
        new Claim("sid", _playSessionId.ToString()),
        new Claim("pid", who.Id.ToString()),
    ], authenticationType: "Test"));

    /// <summary>Every adventure-session endpoint that takes a run (or its campaign) and is not Host-only by policy.</summary>
    private Task<IActionResult> Call(string endpoint, AdventureSessionsController c) => endpoint switch
    {
        "GetSession" => c.GetSession(_runId, default),
        "GetHistory" => c.GetHistory(_runId, default),
        "Recap" => c.Recap(_runId, default),
        "GetAvailableScenes" => c.GetAvailableScenes(_runId, default),
        "GetActiveSession" => c.GetActiveSession(_campaignId, default),
        "Resume" => c.Resume(_runId, default),
        "AdvanceScene" => c.AdvanceScene(_runId, new AdvanceSceneRequest("scene-2"), default),
        "Abandon" => c.Abandon(_runId, default),
        "SubmitPlayerAction" => c.SubmitPlayerAction(_runId, new AdventureSessionActionRequest("I look around"), default),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
    };

    public static TheoryData<string> PlayerVisibleEndpoints => new()
    {
        "GetSession", "GetHistory", "Recap", "GetAvailableScenes", "GetActiveSession",
        "Resume", "AdvanceScene", "Abandon", "SubmitPlayerAction",
    };

    public static TheoryData<string> HostOnlyAtATable => new()
    {
        "GetSession", "GetHistory", "Recap", "GetAvailableScenes", "GetActiveSession",
        "Resume", "AdvanceScene", "Abandon",
    };

    // ── Approved player: acts, and nothing else ───────────────────────────────

    [Theory]
    [MemberData(nameof(HostOnlyAtATable))]
    public async Task ApprovedPlayer_CannotUseTheHostsViewsOrControlsOfAGroupRun(string endpoint)
    {
        await SeedAsync();

        var result = await Call(endpoint, CreateController(Token(_alice)));

        Assert.IsType<ForbidResult>(result);
        var run = await _db.AdventureSessions.AsNoTracking().SingleAsync();
        Assert.Equal(AdventureSessionStatus.InProgress, run.Status);
        Assert.Equal("scene-1", run.CurrentSceneId);
    }

    [Fact]
    public async Task ApprovedPlayer_WhoseTurnItIs_CanAct_AndGetsOnlyTheNarration()
    {
        await SeedAsync();

        var result = await Call("SubmitPlayerAction", CreateController(Token(_alice)));

        var body = Assert.IsType<AdventureSessionActionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("You look around.", body.Narration);
        Assert.Null(body.PrivateDmNote);
        Assert.Empty(body.Actions);
        Assert.Null(body.State);
    }

    [Theory]
    [MemberData(nameof(HostOnlyAtATable))]
    public async Task Host_CanStillUseEveryEndpoint(string endpoint)
    {
        await SeedAsync();
        var host = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Host")], "Test"));

        var result = await Call(endpoint, CreateController(host));

        Assert.IsNotType<ForbidResult>(result);
        Assert.False(result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden }, $"{endpoint} refused the Host.");
    }

    // ── Waiting and denied participants: 403 for everything ───────────────────

    [Theory]
    [MemberData(nameof(PlayerVisibleEndpoints))]
    public async Task ParticipantWaitingForApproval_Gets403WaitingForApproval(string endpoint)
    {
        await SeedAsync();

        var result = await Call(endpoint, CreateController(Token(_waiting)));

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(refused.Value);
        Assert.Equal("Waiting for approval", problem.Title);
        Assert.False(problem.Extensions.ContainsKey("code"));
    }

    [Theory]
    [MemberData(nameof(PlayerVisibleEndpoints))]
    public async Task DeniedParticipant_Gets403WithTheParticipantDeniedCode(string endpoint)
    {
        await SeedAsync();

        var result = await Call(endpoint, CreateController(Token(_denied)));

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("participant-denied", Assert.IsType<ProblemDetails>(refused.Value).Extensions["code"]);
    }

    [Fact]
    public async Task ApprovedPlayer_OfAnotherSession_IsForbiddenFromEveryEndpoint()
    {
        await SeedAsync();
        var stranger = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "Player"),
            new Claim("sid", Guid.NewGuid().ToString()),
            new Claim("pid", _alice.Id.ToString()),   // a real participant id, but not of this session
        ], "Test"));

        foreach (var endpoint in new[] { "GetSession", "SubmitPlayerAction", "Abandon" })
            Assert.IsType<ForbidResult>(await Call(endpoint, CreateController(stranger)));
    }

    // ── Does not run the AI ───────────────────────────────────────────────────

    private sealed class AllowAllPlayerActions : Aircane.Application.Abstractions.IPlayerActionService
    {
        public Task<Aircane.Application.DTOs.Ai.PlayerActionResponse> ProcessActionAsync(
            Aircane.Application.DTOs.Ai.PlayerActionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new Aircane.Application.DTOs.Ai.PlayerActionResponse(
                Narration: "You look around.", PrivateDmNote: "The latch is trapped.", ProposedActions: [], Citations: []));
    }
}
