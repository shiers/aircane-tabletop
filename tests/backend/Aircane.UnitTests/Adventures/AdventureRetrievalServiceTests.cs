using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class AdventureRetrievalServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AdventureRetrievalService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AdventureRetrievalServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _service = new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Helper Methods ────────────────────────────────────────────────────────

    private GeneratedAdventure CreateTestEntity(Guid? id = null)
    {
        var adventureId = id ?? Guid.NewGuid();
        var request = new GenerateAdventureRequest
        {
            Mode = "Group",
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            PartySize = 4,
            AverageLevel = 5,
            Tone = "epic",
            Length = "one-shot",
            Difficulty = "medium",
            CombatRatio = 40,
            ExplorationRatio = 30,
            RoleplayRatio = 30,
        };

        var pitch = new AdventurePitch
        {
            Title = "The Lost Temple",
            Hook = "Ancient evil stirs",
            Summary = "A group must stop a dark ritual.",
        };

        var outline = new AdventureOutline
        {
            SceneSummaries =
            [
                new SceneSummary { Title = "Village", Description = "Starting point", SceneType = "roleplay" },
                new SceneSummary { Title = "Temple", Description = "Final battle", SceneType = "combat" },
            ]
        };

        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "Village", Description = "A quiet village", SceneType = "roleplay", ConnectsTo = ["scene-2"] },
            new() { SceneId = "scene-2", Title = "Temple", Description = "The dark temple", SceneType = "combat", ConnectsTo = [] },
        };

        var npcs = new List<GeneratedNpc>
        {
            new() { Name = "Elder Moira", Role = "quest giver", Personality = "Wise", StatsSummary = "Commoner", SceneIds = ["scene-1"] },
        };

        var encounters = new List<GeneratedEncounter>
        {
            new() { Title = "Temple Guards", SceneId = "scene-2", Enemies = [new EncounterCreature { Name = "Skeleton", Count = 4, ChallengeRating = "1/4" }], Difficulty = "medium", Tactics = "Attack in groups" },
        };

        var treasure = new AdventureTreasure
        {
            GoldTotal = 200,
            Items = [new TreasureItem { Name = "Potion", Description = "Healing potion", SceneId = "scene-2", Value = 50 }],
            MagicItems = [],
        };

        var clues = new AdventureClues
        {
            Secrets = [new AdventureSecret { Title = "Hidden Path", Content = "A secret tunnel", SceneId = "scene-1", DiscoveryMethod = "DC 14 Perception" }],
            Handouts = [],
            FailForwardPaths = [new FailForwardPath { Trigger = "Players stuck", Resolution = "NPC arrives to help", SceneId = "scene-1" }],
        };

        return new GeneratedAdventure
        {
            Id = adventureId,
            Title = pitch.Title,
            Status = GeneratedAdventureStatus.Draft,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            PartyAnalysisJson = null,
            PitchJson = JsonSerializer.Serialize(pitch, JsonOptions),
            OutlineJson = JsonSerializer.Serialize(outline, JsonOptions),
            ScenesJson = JsonSerializer.Serialize(scenes, JsonOptions),
            NpcsJson = JsonSerializer.Serialize(npcs, JsonOptions),
            EncountersJson = JsonSerializer.Serialize(encounters, JsonOptions),
            TreasureJson = JsonSerializer.Serialize(treasure, JsonOptions),
            CluesJson = JsonSerializer.Serialize(clues, JsonOptions),
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private async Task<GeneratedAdventure> SeedAdventureAsync(Guid? id = null)
    {
        var entity = CreateTestEntity(id);
        _db.GeneratedAdventures.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    // ── GetByIdAsync Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_returns_null_when_not_found()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_returns_adventure_with_deserialized_content()
    {
        var entity = await SeedAdventureAsync();

        var result = await _service.GetByIdAsync(entity.Id);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
        Assert.Equal("Draft", result.Status);
        Assert.NotNull(result.Request);
        Assert.Equal("Group", result.Request.Mode);
        Assert.NotNull(result.Pitch);
        Assert.Equal("The Lost Temple", result.Pitch.Title);
        Assert.NotNull(result.Outline);
        Assert.Equal(2, result.Outline.SceneSummaries.Count);
        Assert.NotNull(result.Scenes);
        Assert.Equal(2, result.Scenes.Count);
        Assert.NotNull(result.Npcs);
        Assert.Single(result.Npcs);
        Assert.NotNull(result.Encounters);
        Assert.Single(result.Encounters);
        Assert.NotNull(result.Treasure);
        Assert.Equal(200, result.Treasure.GoldTotal);
        Assert.NotNull(result.Clues);
        Assert.Single(result.Clues.Secrets);
    }

    [Fact]
    public async Task GetByIdAsync_handles_null_party_analysis()
    {
        var entity = await SeedAdventureAsync();

        var result = await _service.GetByIdAsync(entity.Id);

        Assert.NotNull(result);
        Assert.Null(result.PartyAnalysis);
    }

    [Fact]
    public async Task GetByIdAsync_handles_null_optional_json_fields()
    {
        var entity = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "Partial Adventure",
            Status = GeneratedAdventureStatus.Draft,
            RequestJson = JsonSerializer.Serialize(new GenerateAdventureRequest
            {
                Mode = "Solo",
                Ruleset = "D&D 5e 2014",
                GameSystem = "D&D 5e 2014",
                Tone = "dark",
                Length = "short",
                Difficulty = "hard",
            }, JsonOptions),
            PitchJson = null,
            OutlineJson = null,
            ScenesJson = null,
            NpcsJson = null,
            EncountersJson = null,
            TreasureJson = null,
            CluesJson = null,
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.GeneratedAdventures.Add(entity);
        await _db.SaveChangesAsync();

        var result = await _service.GetByIdAsync(entity.Id);

        Assert.NotNull(result);
        Assert.Null(result.Pitch);
        Assert.Null(result.Outline);
        Assert.Null(result.Scenes);
        Assert.Null(result.Npcs);
        Assert.Null(result.Encounters);
        Assert.Null(result.Treasure);
        Assert.Null(result.Clues);
    }

    // ── GetDraftAsync Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDraftAsync_returns_null_when_not_found()
    {
        var result = await _service.GetDraftAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDraftAsync_returns_draft_with_content_sections()
    {
        var entity = await SeedAdventureAsync();

        var result = await _service.GetDraftAsync(entity.Id);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
        Assert.Equal("The Lost Temple", result.Title);
        Assert.Equal("Draft", result.Status);
        Assert.NotNull(result.Pitch);
        Assert.NotNull(result.Outline);
        Assert.NotNull(result.Scenes);
        Assert.NotNull(result.Npcs);
        Assert.NotNull(result.Encounters);
        Assert.NotNull(result.Treasure);
        Assert.NotNull(result.Clues);
    }

    [Fact]
    public async Task GetDraftAsync_preserves_scene_connections()
    {
        var entity = await SeedAdventureAsync();

        var result = await _service.GetDraftAsync(entity.Id);

        Assert.NotNull(result?.Scenes);
        var firstScene = result.Scenes[0];
        Assert.Equal("scene-1", firstScene.SceneId);
        Assert.Contains("scene-2", firstScene.ConnectsTo);
    }
}
