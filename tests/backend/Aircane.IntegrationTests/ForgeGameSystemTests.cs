using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// The token-validating desktop-mode backend, with the job queue swapped for one that only records what is queued, so
/// a test that accepts a generate request does not start a real background generation.
/// </summary>
public sealed class ForgeGameSystemFactory : DesktopModeWebApplicationFactory
{
    public RecordingJobQueue Queue { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBackgroundJobQueue>();
            services.AddSingleton<IBackgroundJobQueue>(Queue);
        });
    }
}

/// <summary>A job queue that remembers what it was given and never hands anything to the worker.</summary>
public sealed class RecordingJobQueue : IBackgroundJobQueue
{
    private readonly List<IBackgroundJob> _jobs = [];

    public IReadOnlyList<AdventureGenerationJobMessage> Generations
    {
        get { lock (_jobs) return _jobs.OfType<AdventureGenerationJobMessage>().ToList(); }
    }

    public ValueTask EnqueueAsync<T>(T job, CancellationToken ct = default) where T : IBackgroundJob
    {
        lock (_jobs) _jobs.Add(job);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The hosted worker waits here for work. Nothing is ever handed to it, so it waits until the host stops,
    /// as it would on a real queue that stays empty.
    /// </summary>
    public async ValueTask<IBackgroundJob> DequeueAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new OperationCanceledException(ct);
    }
}

/// <summary>
/// The Adventure Forge's game system, through the token-validating pipeline (the host is the local machine, anyone else
/// needs a real participant token): the generate request's definition id, and the assign check comparing ids.
/// </summary>
public sealed class ForgeGameSystemTests : IClassFixture<ForgeGameSystemFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly ForgeGameSystemFactory _factory;

    public ForgeGameSystemTests(ForgeGameSystemFactory factory) => _factory = factory;

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private async Task<Guid> SeedDefinitionAsync(string name, string version = "1.0.0", bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var definition = new GameSystemDefinition($"forge-test-{Guid.NewGuid():N}", name, version, schemaVersion: 1, license: "test")
        {
            IsActive = active,
        };
        db.GameSystemDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition.Id;
    }

    private static object GenerateBody(Guid? definitionId = null, string ruleset = "D&D 5e 2014", string gameSystem = "D&D 5e 2014") => new
    {
        mode = "Solo", ruleset, gameSystem, gameSystemDefinitionId = definitionId, partySize = 1, averageLevel = 3,
        tone = "epic", length = "one-shot", difficulty = "medium", combatRatio = 40, explorationRatio = 30, roleplayRatio = 30,
    };

    // ═════════════════════════════════════════════════════════════════════════
    // POST /api/adventures/generate
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_WithAnActiveDefinition_Is202_AndTheQueuedJobCarriesIt_BlankStringsFilledFromIt()
    {
        var id = await SeedDefinitionAsync("Shadowrun Sixth World", version: "6.0");
        using var host = Host();

        using var response = await host.PostAsJsonAsync("/api/adventures/generate", GenerateBody(id, ruleset: "", gameSystem: " "));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = Assert.Single(_factory.Queue.Generations, j => j.Request.GameSystemDefinitionId == id);
        Assert.Equal("Shadowrun Sixth World", job.Request.GameSystem);
        Assert.Equal("6.0", job.Request.Ruleset);
    }

    [Fact]
    public async Task Generate_WithAnUnknownDefinition_Is400_NamingTheField_AndQueuesNothing()
    {
        var before = _factory.Queue.Generations.Count;
        using var host = Host();

        using var response = await host.PostAsJsonAsync("/api/adventures/generate", GenerateBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var messages = problem.GetProperty("errors").GetProperty("GameSystemDefinitionId").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(messages, m => m!.Contains("not found"));
        Assert.Equal(before, _factory.Queue.Generations.Count);
    }

    [Fact]
    public async Task Generate_WithAnInactiveDefinition_Is400()
    {
        var id = await SeedDefinitionAsync("Retired System", active: false);
        using var host = Host();

        using var response = await host.PostAsJsonAsync("/api/adventures/generate", GenerateBody(id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no longer active", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Generate_WithNoDefinitionId_Is202_AsBefore_AndTheStringsAreUntouched()
    {
        using var host = Host();

        using var response = await host.PostAsJsonAsync("/api/adventures/generate", GenerateBody(ruleset: "Homebrew rules 1", gameSystem: "My homebrew"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = Assert.Single(_factory.Queue.Generations, j => j.Request.Ruleset == "Homebrew rules 1");
        Assert.Null(job.Request.GameSystemDefinitionId);
        Assert.Equal("My homebrew", job.Request.GameSystem);
    }

    [Fact]
    public async Task Generate_FromAnotherMachineWithNoToken_IsRefused()
    {
        var before = _factory.Queue.Generations.Count;
        using var stranger = _factory.CreateClientFrom(LanAddress);

        using var response = await stranger.PostAsJsonAsync("/api/adventures/generate", GenerateBody());

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"was {(int)response.StatusCode}");
        Assert.Equal(before, _factory.Queue.Generations.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PATCH /api/adventures/{id}/assign: the mismatch check
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A campaign bound to a definition, with the strings campaign creation would give it (the definition's name and
    /// version). It is seeded directly: creating a campaign through the API does not bind it to a definition.
    /// </summary>
    private async Task<CampaignDto> CreateCampaignAsync(string name, Guid definitionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var definition = await db.GameSystemDefinitions.FindAsync(definitionId);
        var campaign = new Campaign(name, definition!.Name, definition.Version) { GameSystemDefinitionId = definitionId };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        using var host = Host();
        return (await host.GetFromJsonAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", Web))!;
    }

    /// <summary>A campaign with only the strings: the way every campaign is today, and every older one.</summary>
    private async Task<CampaignDto> CreateUnboundCampaignAsync(string name, string gameSystem)
    {
        using var host = Host();
        return (await (await host.PostAsJsonAsync("/api/campaigns", new CreateCampaignRequest(name, gameSystem, "PHB")))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<CampaignDto>(Web))!;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/campaigns/{id}: the definition the campaign is bound to
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ACampaign_ExposesTheDefinitionItIsBoundTo_AndNullWhenItIsNot()
    {
        var definition = await SeedDefinitionAsync("Shadowrun Sixth World", version: "6.0");

        var bound = await CreateCampaignAsync("Bound", definition);
        var unbound = await CreateUnboundCampaignAsync("Unbound", "Homebrew");

        Assert.Equal(definition, bound.GameSystemDefinitionId);
        Assert.Equal("Shadowrun Sixth World", bound.GameSystem);
        Assert.Equal("6.0", bound.Ruleset);
        Assert.Null(unbound.GameSystemDefinitionId);
        using var host = Host();
        var listed = (await host.GetFromJsonAsync<List<CampaignDto>>("/api/campaigns", Web))!;
        Assert.Equal(definition, listed.Single(c => c.Id == bound.Id).GameSystemDefinitionId);
    }

    private async Task<Guid> SeedAdventureAsync(Guid? definitionId, string gameSystem)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var now = DateTime.UtcNow;
        var adventure = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "The Cellar Below",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = JsonSerializer.Serialize(new
            {
                mode = "Group", ruleset = gameSystem, gameSystem, gameSystemDefinitionId = definitionId,
                averageLevel = 3, length = "short", tone = "epic", difficulty = "medium",
            }, Web),
            ScenesJson = "[]",
            Ruleset = gameSystem,
            GameSystem = gameSystem,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedAdventures.Add(adventure);
        await db.SaveChangesAsync();
        return adventure.Id;
    }

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid adventureId, Guid campaignId, bool confirmMismatch = false)
        => client.PatchAsJsonAsync($"/api/adventures/{adventureId}/assign", new { campaignId, confirmMismatch });

    [Fact]
    public async Task Assign_WhenAdventureAndCampaignShareADefinition_NeedsNoConfirmation_WhateverTheAdventureCallsIt()
    {
        var definition = await SeedDefinitionAsync("Shadowrun Sixth World");
        var campaign = await CreateCampaignAsync("Shadowrunners", definition);
        var adventure = await SeedAdventureAsync(definition, gameSystem: "Shadowrun 6e (typed by hand)");
        using var host = Host();

        using var response = await AssignAsync(host, adventure, campaign.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<AdventureListItemDto>(Web);
        Assert.Equal(campaign.Id, item!.CampaignId);
        Assert.Equal(definition, item.GameSystemDefinitionId);
    }

    [Fact]
    public async Task Assign_WhenTheDefinitionsDiffer_NeedsConfirmation_WithTheSameCodeAsBefore()
    {
        var campaignDefinition = await SeedDefinitionAsync("Shadowrun Sixth World");
        var adventureDefinition = await SeedDefinitionAsync("Alien RPG");
        var campaign = await CreateCampaignAsync("Shadowrunners", campaignDefinition);
        var adventure = await SeedAdventureAsync(adventureDefinition, gameSystem: "Alien RPG");
        using var host = Host();

        using var refused = await AssignAsync(host, adventure, campaign.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("game-system-mismatch", problem.GetProperty("code").GetString());
        Assert.Equal("Alien RPG", problem.GetProperty("adventureGameSystem").GetString());
        Assert.Equal("Shadowrun Sixth World", problem.GetProperty("campaignGameSystem").GetString());

        using var confirmed = await AssignAsync(host, adventure, campaign.Id, confirmMismatch: true);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [Fact]
    public async Task Assign_OfAnAdventureWithNoDefinition_StillComparesTheStrings()
    {
        var campaign = await CreateUnboundCampaignAsync("Pathfinders", "Pathfinder Test Edition");
        var same = await SeedAdventureAsync(null, gameSystem: "pathfinder test edition ");
        var other = await SeedAdventureAsync(null, gameSystem: "Call of Cthulhu");
        using var host = Host();

        using var sameResponse = await AssignAsync(host, same, campaign.Id);
        using var otherResponse = await AssignAsync(host, other, campaign.Id);

        Assert.Equal(HttpStatusCode.OK, sameResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherResponse.StatusCode);
    }
}
