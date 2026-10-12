using System.Text.Json;
using Aircane.Api.Controllers;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.BackgroundJobs;
using Aircane.Infrastructure.Persistence;
using Aircane.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>Adventures belong to a campaign: listing them, and generating into one.</summary>
public sealed class CampaignAdventuresTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly ChannelBackgroundJobQueue _queue = new(new InMemoryBackgroundJobStatusStore());
    private readonly AdventuresController _controller;

    public CampaignAdventuresTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _controller = new AdventuresController(
            new GenerateAdventureValidator(),
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            adventureIndexingService: null!, jobQueue: _queue, dbContext: _db,
            logger: NullLogger<AdventuresController>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static GenerateAdventureRequest Request() => new()
    {
        Mode = "Solo", Ruleset = "D&D 5e 2014", GameSystem = "D&D 5e 2014", Tone = "epic",
        Length = "one-shot", Difficulty = "medium", PartySize = 4, AverageLevel = 5,
        CombatRatio = 40, ExplorationRatio = 30, RoleplayRatio = 30,
    };

    private async Task<Campaign> AddCampaignAsync(string name = "Camp")
    {
        var c = new Campaign(name, "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    private async Task<GeneratedAdventure> AddAdventureAsync(
        Guid? campaignId, string title, DateTime createdAt, GeneratedAdventureStatus status = GeneratedAdventureStatus.Draft)
    {
        var a = AdventureSessionTestData.CreateAdventure(status);
        a.Title = title;
        a.CampaignId = campaignId;
        a.CreatedAt = createdAt;
        a.RequestJson = JsonSerializer.Serialize(Request(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _db.GeneratedAdventures.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task ListCampaignAdventures_ReturnsOnlyThisCampaignsAdventures_NewestFirst_WithRequestDetails()
    {
        var mine = await AddCampaignAsync();
        var other = await AddCampaignAsync("Other");
        var older = await AddAdventureAsync(mine.Id, "Older", DateTime.UtcNow.AddHours(-2));
        var newer = await AddAdventureAsync(mine.Id, "Newer", DateTime.UtcNow, GeneratedAdventureStatus.Approved);
        await AddAdventureAsync(other.Id, "Not mine", DateTime.UtcNow);
        await AddAdventureAsync(null, "Unassigned", DateTime.UtcNow);

        var result = await _controller.ListCampaignAdventures(mine.Id, default);

        var items = Assert.IsAssignableFrom<IReadOnlyList<CampaignAdventureListItemDto>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal([newer.Id, older.Id], items.Select(i => i.Id));
        var first = items[0];
        Assert.Equal("Approved", first.Status);
        Assert.Equal("D&D 5e 2014", first.GameSystem);
        Assert.Equal("Solo", first.Mode);
        Assert.Equal("one-shot", first.Length);
        Assert.Equal(5, first.AverageLevel);
    }

    [Fact]
    public async Task ListCampaignAdventures_NoAdventures_ReturnsEmptyList()
    {
        var c = await AddCampaignAsync();

        var items = Assert.IsAssignableFrom<IReadOnlyList<CampaignAdventureListItemDto>>(
            Assert.IsType<OkObjectResult>(await _controller.ListCampaignAdventures(c.Id, default)).Value);

        Assert.Empty(items);
    }

    [Fact]
    public async Task ListCampaignAdventures_UnknownCampaign_ReturnsNotFound()
        => Assert.IsType<NotFoundResult>(await _controller.ListCampaignAdventures(Guid.NewGuid(), default));

    [Fact]
    public async Task ListCampaignAdventures_UnreadableRequestJson_StillListsTheAdventure()
    {
        var c = await AddCampaignAsync();
        var a = await AddAdventureAsync(c.Id, "Odd", DateTime.UtcNow);
        a.RequestJson = "{ not json";
        await _db.SaveChangesAsync();

        var items = Assert.IsAssignableFrom<IReadOnlyList<CampaignAdventureListItemDto>>(
            Assert.IsType<OkObjectResult>(await _controller.ListCampaignAdventures(c.Id, default)).Value);

        var item = Assert.Single(items);
        Assert.Null(item.Mode);
        Assert.Null(item.AverageLevel);
    }

    [Fact]
    public async Task GenerateAdventure_WithCampaign_QueuesJobCarryingTheCampaignId()
    {
        var c = await AddCampaignAsync();

        var result = await _controller.GenerateAdventure(Request(), c.Id, default);

        Assert.IsType<AcceptedResult>(result);
        var job = Assert.IsType<Aircane.Application.Abstractions.BackgroundJobs.AdventureGenerationJobMessage>(await _queue.DequeueAsync(default));
        Assert.Equal(c.Id, job.CampaignId);
    }

    [Fact]
    public async Task GenerateAdventure_WithUnknownCampaign_ReturnsNotFoundAndQueuesNothing()
    {
        var result = await _controller.GenerateAdventure(Request(), Guid.NewGuid(), default);

        Assert.IsType<NotFoundObjectResult>(result);
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _queue.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task DraftDto_ExposesTheCampaignId_SoTheReviewScreenCanGoBack()
    {
        var c = await AddCampaignAsync();
        var a = await AddAdventureAsync(c.Id, "Linked", DateTime.UtcNow);
        var retrieval = new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance);

        Assert.Equal(c.Id, (await retrieval.GetDraftAsync(a.Id))!.CampaignId);
    }
}
