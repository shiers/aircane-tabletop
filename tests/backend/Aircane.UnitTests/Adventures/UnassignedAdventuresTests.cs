using Aircane.Api.Controllers;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Aircane.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>The unassigned-adventures list and assigning an adventure to a campaign.</summary>
public sealed class UnassignedAdventuresTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventuresController _controller;

    public UnassignedAdventuresTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _controller = new AdventuresController(
            validator: null!, retrievalService: null!, adventureIndexingService: null!,
            jobQueue: null!, dbContext: _db, logger: NullLogger<AdventuresController>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Campaign> AddCampaignAsync()
    {
        var c = new Campaign("Camp", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    private async Task<GeneratedAdventure> AddAsync(Guid? campaignId, string title, DateTime createdAt,
        GeneratedAdventureStatus status = GeneratedAdventureStatus.Draft)
    {
        var a = AdventureSessionTestData.CreateAdventure(status);
        a.Title = title;
        a.CampaignId = campaignId;
        a.CreatedAt = createdAt;
        a.UpdatedAt = createdAt;
        _db.GeneratedAdventures.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private async Task<IReadOnlyList<AdventureListItemDto>> UnassignedAsync()
        => Assert.IsAssignableFrom<IReadOnlyList<AdventureListItemDto>>(
            Assert.IsType<OkObjectResult>(await _controller.ListAdventures(null, default, unassigned: true)).Value);

    [Fact]
    public async Task AdventuresController_GetUnassigned_ReturnsOnlyNullCampaignId()
    {
        var older = await AddAsync(null, "Older", DateTime.UtcNow.AddHours(-3));
        var newer = await AddAsync(null, "Newer", DateTime.UtcNow, GeneratedAdventureStatus.Approved);

        var items = await UnassignedAsync();

        Assert.Equal([newer.Id, older.Id], items.Select(i => i.Id));   // newest first
        Assert.All(items, i => Assert.Null(i.CampaignId));
        Assert.Equal("Approved", items[0].Status);
        Assert.Equal("D&D 5e 2014", items[0].GameSystem);
        Assert.Equal("Solo", items[0].Mode);
        Assert.Equal("short", items[0].Length);
        Assert.Equal(3, items[0].AverageLevel);
    }

    [Fact]
    public async Task AdventuresController_GetUnassigned_ExcludesAdventuresWithCampaign()
    {
        var camp = await AddCampaignAsync();
        await AddAsync(camp.Id, "Belongs", DateTime.UtcNow);
        var free = await AddAsync(null, "Free", DateTime.UtcNow);

        Assert.Equal([free.Id], (await UnassignedAsync()).Select(i => i.Id));
    }

    [Fact]
    public async Task ListAdventures_WithoutTheFlag_StillReturnsEverything()
    {
        var camp = await AddCampaignAsync();
        await AddAsync(camp.Id, "Belongs", DateTime.UtcNow);
        await AddAsync(null, "Free", DateTime.UtcNow);

        var all = Assert.IsAssignableFrom<IReadOnlyList<AdventureListItemDto>>(
            Assert.IsType<OkObjectResult>(await _controller.ListAdventures(null, default)).Value);

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task AdventuresController_AssignCampaign_SetsCampaignId()
    {
        var camp = await AddCampaignAsync();
        var adv = await AddAsync(null, "Free", DateTime.UtcNow);

        var result = await _controller.AssignCampaign(adv.Id, new AssignAdventureRequest(camp.Id), default);

        var updated = Assert.IsType<AdventureListItemDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(camp.Id, updated.CampaignId);
        Assert.Equal(adv.Id, updated.Id);
        Assert.Equal(camp.Id, (await _db.GeneratedAdventures.AsNoTracking().SingleAsync(a => a.Id == adv.Id)).CampaignId);
    }

    [Fact]
    public async Task AdventuresController_AssignCampaign_RemovesFromUnassignedResults()
    {
        var camp = await AddCampaignAsync();
        var adv = await AddAsync(null, "Free", DateTime.UtcNow);
        Assert.Single(await UnassignedAsync());

        await _controller.AssignCampaign(adv.Id, new AssignAdventureRequest(camp.Id), default);

        Assert.Empty(await UnassignedAsync());
    }

    [Fact]
    public async Task AdventuresController_AssignCampaign_InvalidCampaign_Returns404()
    {
        var adv = await AddAsync(null, "Free", DateTime.UtcNow);

        var result = await _controller.AssignCampaign(adv.Id, new AssignAdventureRequest(Guid.NewGuid()), default);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Null((await _db.GeneratedAdventures.AsNoTracking().SingleAsync()).CampaignId);   // unchanged
    }

    [Fact]
    public async Task AdventuresController_AssignCampaign_InvalidAdventure_Returns404()
    {
        var camp = await AddCampaignAsync();

        var result = await _controller.AssignCampaign(Guid.NewGuid(), new AssignAdventureRequest(camp.Id), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
