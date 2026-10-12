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

/// <summary>The saved conversation of an adventure run, and reading it back.</summary>
public sealed class AdventureHistoryTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventureSessionService _service;
    private readonly AdventureSessionsController _controller;
    private Guid _runId;
    private Guid _campaignId;

    public AdventureHistoryTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance);
        _controller = new AdventureSessionsController(
            _service, playerActionService: null!, sessionHosting: null!, NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedRunAsync()
    {
        var campaign = new Campaign("C", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.Campaigns.Add(campaign);
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        _runId = (await _service.StartAdventureSessionAsync(adventure.Id, campaign.Id, null)).Id;
        _campaignId = campaign.Id;
    }

    private async Task RecordAsync(int exchanges)
    {
        for (var i = 1; i <= exchanges; i++)
            await _service.RecordExchangeAsync(_runId, $"action {i}", $"narration {i}", "scene-1");
    }

    private async Task<IReadOnlyList<AdventureHistoryEntryDto>> HistoryAsync()
        => Assert.IsAssignableFrom<IReadOnlyList<AdventureHistoryEntryDto>>(
            Assert.IsType<OkObjectResult>(await _controller.GetHistory(_runId, default)).Value);

    [Fact]
    public async Task AdventureSessionsController_GetHistory_ReturnsLast50Entries()
    {
        await SeedRunAsync();
        await RecordAsync(30);   // 60 entries

        var history = await HistoryAsync();

        Assert.Equal(50, history.Count);
        // The oldest 10 entries (5 exchanges) are dropped; the newest is kept.
        Assert.Equal("action 6", history[0].Content);
        Assert.Equal("narration 30", history[^1].Content);
    }

    [Fact]
    public async Task AdventureSessionsController_GetHistory_OrderedAscending()
    {
        await SeedRunAsync();
        await RecordAsync(3);

        var history = await HistoryAsync();

        Assert.Equal(["action 1", "narration 1", "action 2", "narration 2", "action 3", "narration 3"],
            history.Select(h => h.Content));
        Assert.Equal(["PlayerAction", "AiNarration", "PlayerAction", "AiNarration", "PlayerAction", "AiNarration"],
            history.Select(h => h.EntryType));
        Assert.Equal(history.OrderBy(h => h.CreatedAt).Select(h => h.Id), history.Select(h => h.Id));
    }

    [Fact]
    public async Task GetHistory_ReturnsAllEntriesWhenFewerThan50()
    {
        await SeedRunAsync();
        await RecordAsync(2);

        Assert.Equal(4, (await HistoryAsync()).Count);
    }

    [Fact]
    public async Task GetHistory_NewRun_IsEmpty()
    {
        await SeedRunAsync();

        Assert.Empty(await HistoryAsync());
    }

    [Fact]
    public async Task GetHistory_IncludesSceneAndIsolatesRuns()
    {
        await SeedRunAsync();
        await RecordAsync(1);
        // Another run's entries never leak in.
        _db.AdventureSessionEntries.Add(new AdventureSessionEntry
        {
            Id = Guid.NewGuid(), AdventureSessionId = Guid.NewGuid(), EntryType = AdventureEntryType.AiNarration,
            Content = "someone else", CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var history = await HistoryAsync();

        Assert.DoesNotContain(history, h => h.Content == "someone else");
        Assert.All(history, h => Assert.Equal("scene-1", h.SceneId));
    }

    [Fact]
    public async Task GetHistory_UnknownRun_Returns404()
    {
        await SeedRunAsync();

        Assert.IsType<NotFoundResult>(await _controller.GetHistory(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task GetHistory_PlayerOfAnotherSession_IsForbidden()
    {
        await SeedRunAsync();
        _controller.ControllerContext.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Player"), new Claim("sid", Guid.NewGuid().ToString())], "Test"));

        Assert.IsType<ForbidResult>(await _controller.GetHistory(_runId, default));
    }

    [Fact]
    public async Task ListInProgress_ReturnsOnlyRunningRuns_MostRecentFirst()
    {
        await SeedRunAsync();
        var otherCampaign = new Campaign("C2", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        var otherAdventure = AdventureSessionTestData.CreateAdventure();
        _db.Campaigns.Add(otherCampaign);
        _db.GeneratedAdventures.Add(otherAdventure);
        await _db.SaveChangesAsync();
        var second = await _service.StartAdventureSessionAsync(otherAdventure.Id, otherCampaign.Id, null);
        var finished = new Campaign("C3", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(finished);
        await _db.SaveChangesAsync();
        await _service.AbandonAdventureAsync((await _service.StartAdventureSessionAsync(otherAdventure.Id, finished.Id, null)).Id);

        var result = Assert.IsType<OkObjectResult>(await _controller.ListInProgress(default));
        var runs = Assert.IsAssignableFrom<IReadOnlyList<AdventureSessionDto>>(result.Value);

        Assert.Equal([second.Id, _runId], runs.Select(r => r.Id));
        Assert.All(runs, r => Assert.Equal("InProgress", r.Status));
        _ = _campaignId;
    }
}
