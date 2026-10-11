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
/// Callers must be the Host, or the solo Player whose participant token was issued for the run's
/// play session. The integration-test auth handler always signs in as Host, so the Player/other
/// paths are exercised here with explicit claims.
/// </summary>
public sealed class AdventureSessionsControllerAuthorizationTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventureSessionService _service;
    private Guid _runId;
    private Guid _playSessionId;
    private Guid _campaignId;
    private Guid _adventureId;

    public AdventureSessionsControllerAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
        _service = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedRunAsync()
    {
        var campaign = new Campaign("C", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.Campaigns.Add(campaign);
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();

        var run = await _service.StartAdventureSessionAsync(adventure.Id, campaign.Id, null);
        _playSessionId = Guid.NewGuid();
        await _service.AttachPlaySessionAsync(run.Id, _playSessionId);
        _runId = run.Id;
        _campaignId = campaign.Id;
        _adventureId = adventure.Id;
    }

    private AdventureSessionsController CreateController(ClaimsPrincipal? user)
    {
        var controller = new AdventureSessionsController(
            _service,
            playerActionService: null!,
            sessionHosting: null!,
            NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) },
            },
        };
        return controller;
    }

    private static ClaimsPrincipal Participant(string role, Guid sessionId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, role), new Claim("sid", sessionId.ToString())],
        authenticationType: "Test"));

    // ── Reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSession_Host_IsAllowed()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Host", Guid.NewGuid())).GetSession(_runId, default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetSession_PlayerOfTheRunsPlaySession_IsAllowed()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", _playSessionId)).GetSession(_runId, default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetSession_PlayerOfAnotherSession_IsForbidden()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", Guid.NewGuid())).GetSession(_runId, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("HumanDm")]
    [InlineData("Spectator")]
    public async Task GetSession_NonHostNonPlayerRoles_AreForbidden(string role)
    {
        await SeedRunAsync();

        var result = await CreateController(Participant(role, _playSessionId)).GetSession(_runId, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetSession_Unauthenticated_IsAllowedBecauseThePolicyAlreadyGatedIt()
    {
        // Reaching the action unauthenticated only happens when the "Authenticated" policy is
        // permissive (Development / TrustLocalHost), where the single local user is the host.
        await SeedRunAsync();

        var result = await CreateController(user: null).GetSession(_runId, default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetActiveSession_PlayerOfAnotherSession_IsForbidden()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", Guid.NewGuid())).GetActiveSession(_campaignId, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ── Writes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceScene_PlayerOfAnotherSession_IsForbiddenAndStateIsUnchanged()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", Guid.NewGuid()))
            .AdvanceScene(_runId, new AdvanceSceneRequest("scene-2"), default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("scene-1", (await _db.AdventureSessions.AsNoTracking().SingleAsync()).CurrentSceneId);
    }

    [Fact]
    public async Task Abandon_PlayerOfTheRunsPlaySession_IsAllowed()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", _playSessionId)).Abandon(_runId, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AdventureSessionStatus.Abandoned, (await _db.AdventureSessions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task StartSession_Player_IsForbidden()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", _playSessionId))
            .StartSession(new StartAdventureSessionRequest(_adventureId, _campaignId), default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetSession_UnknownRun_ReturnsNotFoundBeforeAnyAuthorizationDecision()
    {
        await SeedRunAsync();

        var result = await CreateController(Participant("Player", Guid.NewGuid())).GetSession(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }
}

/// <summary>
/// The player-action endpoint: the DM-only private note never reaches a Player token, and the
/// per-action outcomes are reported with names rather than enum numbers.
/// </summary>
public sealed class AdventureSessionsControllerPlayerActionTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventureSessionService _service;

    public AdventureSessionsControllerPlayerActionTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
        _service = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private sealed class StubPlayerActionService : Aircane.Application.Abstractions.IPlayerActionService
    {
        public Task<Aircane.Application.DTOs.Ai.PlayerActionResponse> ProcessActionAsync(
            Aircane.Application.DTOs.Ai.PlayerActionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new Aircane.Application.DTOs.Ai.PlayerActionResponse(
                Narration: "You pry open the chest.",
                PrivateDmNote: "It is trapped.",
                ProposedActions:
                [
                    new Aircane.Application.DTOs.Ai.ProposedActionResult(
                        Aircane.Application.Abstractions.AiActionType.AdvanceScene,
                        Label: null,
                        Aircane.Application.Abstractions.StateCommandOutcome.PermissionDenied,
                        ErrorMessage: "Needs Full Session Control."),
                ],
                Citations: []));
    }

    private async Task<(AdventureSessionsController Controller, Guid RunId, Guid PlaySessionId)> SetUpAsync(ClaimsPrincipal user)
    {
        var campaign = new Campaign("C", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.Campaigns.Add(campaign);
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        var run = await _service.StartAdventureSessionAsync(adventure.Id, campaign.Id, null);
        var playSessionId = Guid.NewGuid();
        await _service.AttachPlaySessionAsync(run.Id, playSessionId);

        var controller = new AdventureSessionsController(
            _service, new StubPlayerActionService(), sessionHosting: null!,
            NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };
        return (controller, run.Id, playSessionId);
    }

    private static ClaimsPrincipal Participant(string role, Guid? sessionId = null) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, role), new Claim("sid", (sessionId ?? Guid.NewGuid()).ToString())], "Test"));

    [Fact]
    public async Task PlayerAction_Host_ReceivesPrivateDmNote()
    {
        var (controller, runId, _) = await SetUpAsync(Participant("Host"));

        var result = await controller.SubmitPlayerAction(runId, new AdventureSessionActionRequest("I pry it open"), default);

        var body = Assert.IsType<AdventureSessionActionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("It is trapped.", body.PrivateDmNote);
    }

    [Fact]
    public async Task PlayerAction_SoloPlayer_NeverReceivesPrivateDmNote()
    {
        // The player's token is issued for the run's own play session, so it may act, but the
        // DM-only note must be withheld.
        var playSessionId = Guid.NewGuid();
        var (controller, runId, actualPlaySession) = await SetUpAsync(Participant("Player", playSessionId));
        // Re-bind the token to the session the service actually attached.
        controller.ControllerContext.HttpContext!.User = Participant("Player", actualPlaySession);

        var result = await controller.SubmitPlayerAction(runId, new AdventureSessionActionRequest("I pry it open"), default);

        var body = Assert.IsType<AdventureSessionActionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(body.PrivateDmNote);
        Assert.Equal("You pry open the chest.", body.Narration);
    }

    [Fact]
    public async Task PlayerAction_ReportsActionOutcomesByName()
    {
        var (controller, runId, _) = await SetUpAsync(Participant("Host"));

        var result = await controller.SubmitPlayerAction(runId, new AdventureSessionActionRequest("I pry it open"), default);

        var body = Assert.IsType<AdventureSessionActionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var action = Assert.Single(body.Actions);
        Assert.Equal("AdvanceScene", action.ActionType);
        Assert.Equal("PermissionDenied", action.Outcome);
        Assert.Equal("Needs Full Session Control.", action.ErrorMessage);
        Assert.Equal("scene-1", body.State.CurrentScene.SceneId);
    }

    [Fact]
    public async Task PlayerAction_PlayerOfAnotherSession_IsForbidden()
    {
        var (controller, runId, _) = await SetUpAsync(Participant("Player"));   // random session id

        var result = await controller.SubmitPlayerAction(runId, new AdventureSessionActionRequest("hi"), default);

        Assert.IsType<ForbidResult>(result);
    }
}
