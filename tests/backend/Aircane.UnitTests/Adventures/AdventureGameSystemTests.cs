using System.Text.Json;
using Aircane.Api.Controllers;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.BackgroundJobs;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>
/// The Adventure Forge knows which game system it generates for: an optional definition id on the generate request
/// (validated, and filling a blank ruleset or game system), and the assign-to-campaign check comparing definition ids
/// when both sides have one. Requests and adventures without an id keep working as they did.
/// </summary>
public sealed class AdventureGameSystemTests : IDisposable
{
    private const string DndName = "Dungeons & Dragons 5th Edition (2014)";
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AircaneDbContext _db;
    private readonly ChannelBackgroundJobQueue _queue = new(new InMemoryBackgroundJobStatusStore());
    private readonly AdventuresController _controller;

    public AdventureGameSystemTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);

        _controller = new AdventuresController(
            new GenerateAdventureValidator(), retrievalService: null!, adventureIndexingService: null!,
            jobQueue: _queue, dbContext: _db, gameSystems: new GameSystemCanonicalizer(_db),
            logger: NullLogger<AdventuresController>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Set-up ────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedDefinitionAsync(string identifier, string name, string version = "1.0.0", bool active = true)
    {
        var definition = new GameSystemDefinition(identifier, name, version, schemaVersion: 1, license: "built-in") { IsActive = active };
        _db.GameSystemDefinitions.Add(definition);
        await _db.SaveChangesAsync();
        return definition.Id;
    }

    private static GenerateAdventureRequest Request(
        Guid? definitionId = null, string ruleset = "D&D 5e 2014", string gameSystem = "D&D 5e 2014") => new()
    {
        Mode = "Solo", Ruleset = ruleset, GameSystem = gameSystem, GameSystemDefinitionId = definitionId,
        Tone = "epic", Length = "one-shot", Difficulty = "medium", PartySize = 1, AverageLevel = 3,
        CombatRatio = 40, ExplorationRatio = 30, RoleplayRatio = 30,
    };

    private async Task<AdventureGenerationJobMessage> QueuedAsync()
    {
        using var cts = new CancellationTokenSource(2000);
        return Assert.IsType<AdventureGenerationJobMessage>(await _queue.DequeueAsync(cts.Token));
    }

    private async Task AssertNothingQueuedAsync()
    {
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _queue.DequeueAsync(cts.Token));
    }

    private static ValidationProblemDetails ProblemOf(IActionResult result)
        => Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);

    // ═════════════════════════════════════════════════════════════════════════
    // Generate: the definition id
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_WithAnActiveDefinition_IsAccepted_AndTheJobCarriesTheId()
    {
        var id = await SeedDefinitionAsync("dnd-5e-2014", DndName);

        var result = await _controller.GenerateAdventure(Request(id), campaignId: null, default);

        Assert.IsType<AcceptedResult>(result);
        var job = await QueuedAsync();
        Assert.Equal(id, job.Request.GameSystemDefinitionId);
    }

    [Fact]
    public async Task Generate_WithADefinition_KeepsTheRulesetAndGameSystemItWasSent()
    {
        var id = await SeedDefinitionAsync("dnd-5e-2014", DndName);

        await _controller.GenerateAdventure(Request(id, ruleset: "My ruleset", gameSystem: "My system"), null, default);

        var job = await QueuedAsync();
        Assert.Equal("My ruleset", job.Request.Ruleset);
        Assert.Equal("My system", job.Request.GameSystem);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "\t")]
    public async Task Generate_WithADefinition_FillsBlankStringsFromIt_TheWayCampaignCreationDoes(string ruleset, string gameSystem)
    {
        var id = await SeedDefinitionAsync("dnd-5e-2014", DndName, version: "1.0.0");

        var result = await _controller.GenerateAdventure(Request(id, ruleset, gameSystem), null, default);

        Assert.IsType<AcceptedResult>(result);
        var job = await QueuedAsync();
        Assert.Equal(DndName, job.Request.GameSystem);   // the definition's name
        Assert.Equal("1.0.0", job.Request.Ruleset);      // and its version
    }

    [Fact]
    public async Task Generate_WithADefinition_FillsOnlyTheBlankString()
    {
        var id = await SeedDefinitionAsync("dnd-5e-2014", DndName);

        await _controller.GenerateAdventure(Request(id, ruleset: "", gameSystem: "Typed system"), null, default);

        var job = await QueuedAsync();
        Assert.Equal("Typed system", job.Request.GameSystem);
        Assert.Equal("1.0.0", job.Request.Ruleset);
    }

    [Fact]
    public async Task Generate_WithAnUnknownDefinitionId_Is400_AndQueuesNothing()
    {
        var result = await _controller.GenerateAdventure(Request(Guid.NewGuid()), null, default);

        var problem = ProblemOf(result);
        Assert.Contains("not found", Assert.Single(problem.Errors["GameSystemDefinitionId"]));
        await AssertNothingQueuedAsync();
    }

    [Fact]
    public async Task Generate_WithAnInactiveDefinition_Is400_AndQueuesNothing()
    {
        var id = await SeedDefinitionAsync("retired", "Retired System", active: false);

        var result = await _controller.GenerateAdventure(Request(id), null, default);

        Assert.Contains("no longer active", Assert.Single(ProblemOf(result).Errors["GameSystemDefinitionId"]));
        await AssertNothingQueuedAsync();
    }

    [Fact]
    public async Task Generate_WithAnEmptyDefinitionId_Is400()
    {
        var result = await _controller.GenerateAdventure(Request(Guid.Empty), null, default);

        Assert.Contains("must not be empty", Assert.Single(ProblemOf(result).Errors["GameSystemDefinitionId"]));
        await AssertNothingQueuedAsync();
    }

    [Fact]
    public async Task Generate_ReportsABadDefinitionAndOtherProblemsTogether()
    {
        var result = await _controller.GenerateAdventure(Request(Guid.NewGuid()) with { PartySize = 0 }, null, default);

        var errors = ProblemOf(result).Errors;
        Assert.Contains("GameSystemDefinitionId", errors.Keys);
        Assert.Contains("PartySize", errors.Keys);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Generate: requests with no definition id work as before
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_WithoutADefinitionId_WorksAsBefore_EvenWithNoDatabase()
    {
        // The controller of the existing generate tests has no database at all: a request with no definition id must
        // not touch it.
        var controller = new AdventuresController(
            new GenerateAdventureValidator(), retrievalService: null!, adventureIndexingService: null!,
            jobQueue: _queue, dbContext: null!, gameSystems: null!, logger: NullLogger<AdventuresController>.Instance);

        var result = await controller.GenerateAdventure(Request(), null, default);

        Assert.IsType<AcceptedResult>(result);
        var job = await QueuedAsync();
        Assert.Null(job.Request.GameSystemDefinitionId);
        Assert.Equal("D&D 5e 2014", job.Request.Ruleset);
        Assert.Equal("D&D 5e 2014", job.Request.GameSystem);
    }

    [Theory]
    [InlineData("", "D&D 5e 2014", "Ruleset")]
    [InlineData("D&D 5e 2014", "  ", "GameSystem")]
    public async Task Generate_WithoutADefinitionId_StillRequiresBothStrings(string ruleset, string gameSystem, string property)
    {
        var result = await _controller.GenerateAdventure(Request(null, ruleset, gameSystem), null, default);

        Assert.Contains(property, ProblemOf(result).Errors.Keys);
        await AssertNothingQueuedAsync();
    }

    [Fact]
    public void TheStoredRequest_CarriesTheId_AndAnOldOneWithoutItStillReads()
    {
        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(Request(id), Camel);
        Assert.Contains($"\"gameSystemDefinitionId\":\"{id}\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize<GenerateAdventureRequest>(json, Camel)!.GameSystemDefinitionId);

        const string old = """
            {"mode":"Group","ruleset":"D&D 5e 2014","gameSystem":"D&D 5e 2014","partySize":4,"averageLevel":3,
             "tone":"epic","length":"short","difficulty":"medium","combatRatio":40,"explorationRatio":30,"roleplayRatio":30}
            """;
        var read = JsonSerializer.Deserialize<GenerateAdventureRequest>(old, Camel)!;
        Assert.Null(read.GameSystemDefinitionId);
        Assert.Equal("D&D 5e 2014", read.GameSystem);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Assign: the game system mismatch check
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<(Guid AdventureId, Guid CampaignId)> SeedPairAsync(
        string adventureSystem, Guid? adventureDefinitionId,
        string campaignSystem, Guid? campaignDefinitionId,
        string? requestJson = null)
    {
        var now = DateTime.UtcNow;
        var adventure = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "The Cellar Below",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = requestJson ?? JsonSerializer.Serialize(Request(adventureDefinitionId, adventureSystem, adventureSystem), Camel),
            ScenesJson = "[]",
            Ruleset = adventureSystem,
            GameSystem = adventureSystem,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var campaign = new Campaign("Table", campaignSystem, "PHB") { GameSystemDefinitionId = campaignDefinitionId };
        _db.GeneratedAdventures.Add(adventure);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();
        return (adventure.Id, campaign.Id);
    }

    private Task<IActionResult> AssignAsync(Guid adventureId, Guid campaignId, bool confirmMismatch = false)
        => _controller.AssignCampaign(adventureId, new AssignAdventureRequest(campaignId, ConfirmMismatch: confirmMismatch), default);

    private static string MismatchCode(IActionResult result)
    {
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(result).Value);
        return Assert.IsType<string>(problem.Extensions["code"]);
    }

    [Fact]
    public async Task Assign_WhenBothCarryTheSameDefinition_IsNotAMismatch_WhateverTheStringsSay()
    {
        var id = Guid.NewGuid();
        var (adventure, campaign) = await SeedPairAsync("Shadowrun 6th", id, "A different spelling entirely", id);

        Assert.IsType<OkObjectResult>(await AssignAsync(adventure, campaign));
    }

    [Fact]
    public async Task Assign_WhenBothCarryDifferentDefinitions_IsAMismatch_EvenWhenTheStringsAreEqual()
    {
        var (adventure, campaign) = await SeedPairAsync("Same words", Guid.NewGuid(), "Same words", Guid.NewGuid());

        var refused = await AssignAsync(adventure, campaign);

        Assert.Equal("game-system-mismatch", MismatchCode(refused));
        Assert.Null((await _db.GeneratedAdventures.AsNoTracking().SingleAsync()).CampaignId);

        // The confirm flag still lets it through.
        Assert.IsType<OkObjectResult>(await AssignAsync(adventure, campaign, confirmMismatch: true));
        Assert.Equal(campaign, (await _db.GeneratedAdventures.AsNoTracking().SingleAsync()).CampaignId);
    }

    [Theory]
    [InlineData(true, false)]   // the adventure has a definition, the campaign has none
    [InlineData(false, true)]   // the campaign has one, the adventure has none (every older adventure)
    public async Task Assign_WhenOnlyOneSideHasADefinition_ComparesTheStrings_AsBefore(bool adventureHasId, bool campaignHasId)
    {
        var id = Guid.NewGuid();
        var (same, sameCampaign) = await SeedPairAsync("D&D 5e 2014", adventureHasId ? id : null, "d&d 5e 2014 ", campaignHasId ? id : null);
        Assert.IsType<OkObjectResult>(await AssignAsync(same, sameCampaign));

        var (other, otherCampaign) = await SeedPairAsync("D&D 5e 2014", adventureHasId ? id : null, "Pathfinder 2e", campaignHasId ? id : null);
        Assert.Equal("game-system-mismatch", MismatchCode(await AssignAsync(other, otherCampaign)));
    }

    [Fact]
    public async Task Assign_OfAnAdventureWithAnUnreadableRequest_ComparesTheStrings_AndDoesNotThrow()
    {
        var id = Guid.NewGuid();
        foreach (var requestJson in new[] { "not json at all", "{}", "[]", """{"gameSystemDefinitionId":"not-a-guid"}""", """{"gameSystemDefinitionId":null}""" })
        {
            var (adventure, campaign) = await SeedPairAsync("D&D 5e 2014", null, "D&D 5e 2014", id, requestJson);

            Assert.IsType<OkObjectResult>(await AssignAsync(adventure, campaign));
        }
    }

    [Fact]
    public async Task Assign_KeepsItsOtherRefusals_AndTheirCodes()
    {
        var id = Guid.NewGuid();
        var (adventure, campaign) = await SeedPairAsync("D&D 5e 2014", id, "D&D 5e 2014", id);
        var other = new Campaign("Elsewhere", "D&D 5e 2014", "PHB") { GameSystemDefinitionId = id };
        _db.Campaigns.Add(other);
        await _db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await AssignAsync(adventure, campaign));

        // Moving it to another campaign still needs confirmMove, and says so with its own code.
        var result = await _controller.AssignCampaign(adventure, new AssignAdventureRequest(other.Id), default);

        Assert.Equal("adventure-in-other-campaign", MismatchCode(result));
        Assert.IsType<NotFoundObjectResult>(await _controller.AssignCampaign(Guid.NewGuid(), new AssignAdventureRequest(campaign), default));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The adventure list shows the definition
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TheAdventureList_ExposesTheDefinitionId_NullWhenThereIsNone()
    {
        var id = Guid.NewGuid();
        var (withId, _) = await SeedPairAsync("A", id, "A", null);
        var (without, _) = await SeedPairAsync("B", null, "B", null, requestJson: "{}");

        var ok = Assert.IsType<OkObjectResult>(await _controller.ListAdventures(null, default));

        var items = Assert.IsAssignableFrom<IEnumerable<AdventureListItemDto>>(ok.Value).ToList();
        Assert.Equal(id, items.Single(i => i.Id == withId).GameSystemDefinitionId);
        Assert.Null(items.Single(i => i.Id == without).GameSystemDefinitionId);
    }
}
