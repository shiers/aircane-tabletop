using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class AdventureGenerationServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly StructuredFakeAiProvider _fakeAi;
    private readonly AdventureGenerationService _service;

    public AdventureGenerationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _fakeAi = new StructuredFakeAiProvider();
        _service = new AdventureGenerationService(
            _fakeAi, _db, NullLogger<AdventureGenerationService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static GenerateAdventureRequest CreateRequest(string mode = "Group") => new()
    {
        Mode = mode,
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
        Setting = "Forgotten Realms",
    };

    private static PartyAnalysisResult CreatePartyAnalysis() => new()
    {
        PartySize = 4,
        AverageLevel = 5.0,
        MinLevel = 5,
        MaxLevel = 5,
        Classes = new Dictionary<string, int> { ["Fighter"] = 1, ["Cleric"] = 1, ["Rogue"] = 1, ["Wizard"] = 1 },
        AverageAC = 15.75,
        AverageHP = 35.5,
        TotalHP = 142,
        HasHealing = true,
        HasRangedAttacks = true,
        HasMagic = true,
        HasStealth = true,
        HasPerception = true,
        Capabilities = ["Party has healing capability", "Party has ranged attack options"],
        Weaknesses = [],
    };

    // ── Full Pipeline Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_runs_all_stages_and_returns_complete_adventure()
    {
        var request = CreateRequest();
        var partyAnalysis = CreatePartyAnalysis();

        var result = await _service.GenerateAsync(request, partyAnalysis);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Draft", result.Status);
        Assert.NotNull(result.Pitch);
        Assert.NotNull(result.Outline);
        Assert.NotNull(result.Scenes);
        Assert.NotNull(result.Npcs);
        Assert.NotNull(result.Encounters);
        Assert.NotNull(result.Treasure);
        Assert.NotNull(result.Clues);
        Assert.Equal(request, result.Request);
        Assert.Equal(partyAnalysis, result.PartyAnalysis);
    }

    [Fact]
    public async Task GenerateAsync_persists_adventure_to_database()
    {
        var request = CreateRequest();

        var result = await _service.GenerateAsync(request, null);

        var entity = await _db.GeneratedAdventures.FindAsync(result.Id);
        Assert.NotNull(entity);
        Assert.Equal(GeneratedAdventureStatus.Draft, entity.Status);
        Assert.Equal("D&D 5e 2014", entity.Ruleset);
        Assert.Equal("D&D 5e 2014", entity.GameSystem);
        Assert.NotNull(entity.PitchJson);
        Assert.NotNull(entity.OutlineJson);
        Assert.NotNull(entity.ScenesJson);
        Assert.NotNull(entity.NpcsJson);
        Assert.NotNull(entity.EncountersJson);
        Assert.NotNull(entity.TreasureJson);
        Assert.NotNull(entity.CluesJson);
    }

    [Fact]
    public async Task GenerateAsync_without_party_analysis_sets_null()
    {
        var request = CreateRequest();

        var result = await _service.GenerateAsync(request, null);

        Assert.Null(result.PartyAnalysis);
        var entity = await _db.GeneratedAdventures.FindAsync(result.Id);
        Assert.Null(entity!.PartyAnalysisJson);
    }

    [Fact]
    public async Task GenerateAsync_with_party_analysis_stores_it()
    {
        var request = CreateRequest();
        var partyAnalysis = CreatePartyAnalysis();

        var result = await _service.GenerateAsync(request, partyAnalysis);

        var entity = await _db.GeneratedAdventures.FindAsync(result.Id);
        Assert.NotNull(entity!.PartyAnalysisJson);
    }

    [Fact]
    public async Task GenerateAsync_sets_title_from_pitch()
    {
        var request = CreateRequest();

        var result = await _service.GenerateAsync(request, null);

        Assert.Equal(result.Pitch!.Title, result.Pitch.Title);
        var entity = await _db.GeneratedAdventures.FindAsync(result.Id);
        Assert.Equal(result.Pitch.Title, entity!.Title);
    }

    [Fact]
    public async Task GenerateAsync_calls_ai_provider_seven_times()
    {
        var request = CreateRequest();

        await _service.GenerateAsync(request, null);

        // 7 stages: pitch, outline, scenes, npcs, encounters, clues, treasure
        Assert.Equal(7, _fakeAi.CallCount);
    }

    // ── Individual Stage Tests ────────────────────────────────────────────────

    [Fact]
    public async Task RegeneratePitchAsync_returns_valid_pitch()
    {
        var request = CreateRequest();

        var pitch = await _service.RegeneratePitchAsync(request, null);

        Assert.NotNull(pitch);
        Assert.False(string.IsNullOrWhiteSpace(pitch.Title));
        Assert.False(string.IsNullOrWhiteSpace(pitch.Hook));
        Assert.False(string.IsNullOrWhiteSpace(pitch.Summary));
    }

    [Fact]
    public async Task RegenerateOutlineAsync_returns_valid_outline()
    {
        var request = CreateRequest();
        var pitch = new AdventurePitch
        {
            Title = "Test Adventure",
            Hook = "A test hook",
            Summary = "A test summary",
        };

        var outline = await _service.RegenerateOutlineAsync(request, null, pitch);

        Assert.NotNull(outline);
        Assert.NotEmpty(outline.SceneSummaries);
    }

    [Fact]
    public async Task RegenerateScenesAsync_returns_valid_scenes()
    {
        var request = CreateRequest();
        var pitch = new AdventurePitch { Title = "Test", Hook = "Hook", Summary = "Summary" };
        var outline = new AdventureOutline
        {
            SceneSummaries = [new SceneSummary { Title = "Scene 1", Description = "Desc", SceneType = "combat" }]
        };

        var scenes = await _service.RegenerateScenesAsync(request, null, pitch, outline);

        Assert.NotNull(scenes);
        Assert.NotEmpty(scenes);
    }

    [Fact]
    public async Task RegenerateNpcsAsync_returns_valid_npcs()
    {
        var request = CreateRequest();
        var pitch = new AdventurePitch { Title = "Test", Hook = "Hook", Summary = "Summary" };
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "Tavern", Description = "A tavern", SceneType = "roleplay", ConnectsTo = ["scene-2"] }
        };

        var npcs = await _service.RegenerateNpcsAsync(request, null, pitch, scenes);

        Assert.NotNull(npcs);
        Assert.NotEmpty(npcs);
    }

    [Fact]
    public async Task RegenerateEncountersAsync_returns_valid_encounters()
    {
        var request = CreateRequest();
        var pitch = new AdventurePitch { Title = "Test", Hook = "Hook", Summary = "Summary" };
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "Ambush", Description = "An ambush", SceneType = "combat", ConnectsTo = [] }
        };

        var encounters = await _service.RegenerateEncountersAsync(request, null, pitch, scenes);

        Assert.NotNull(encounters);
        Assert.NotEmpty(encounters);
    }

    [Fact]
    public async Task RegenerateTreasureAsync_returns_valid_treasure()
    {
        var request = CreateRequest();
        var pitch = new AdventurePitch { Title = "Test", Hook = "Hook", Summary = "Summary" };
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "Vault", Description = "A vault", SceneType = "exploration", ConnectsTo = [] }
        };
        var encounters = new List<GeneratedEncounter>();

        var treasure = await _service.RegenerateTreasureAsync(request, null, pitch, scenes, encounters);

        Assert.NotNull(treasure);
        Assert.True(treasure.GoldTotal >= 0);
    }

    [Fact]
    public async Task RegenerateCluesAsync_returns_valid_clues()
    {
        var request = CreateRequest();
        var pitch = new AdventurePitch { Title = "Test", Hook = "Hook", Summary = "Summary" };
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "Library", Description = "A library", SceneType = "exploration", ConnectsTo = [] }
        };
        var npcs = new List<GeneratedNpc>
        {
            new() { Name = "Sage", Role = "informant", Personality = "Wise", StatsSummary = "Commoner", SceneIds = ["scene-1"] }
        };

        var clues = await _service.RegenerateCluesAsync(request, null, pitch, scenes, npcs);

        Assert.NotNull(clues);
        Assert.NotNull(clues.Secrets);
        Assert.NotNull(clues.Handouts);
        Assert.NotNull(clues.FailForwardPaths);
    }

    // ── JSON Parsing Tests ────────────────────────────────────────────────────

    [Fact]
    public void ParseJsonResponse_handles_valid_json()
    {
        var json = """{"title":"Test","hook":"A hook","summary":"A summary"}""";

        var result = AdventureGenerationService.ParseJsonResponse<AdventurePitch>(json, "pitch");

        Assert.Equal("Test", result.Title);
        Assert.Equal("A hook", result.Hook);
        Assert.Equal("A summary", result.Summary);
    }

    [Fact]
    public void ParseJsonResponse_strips_markdown_code_fences()
    {
        var json = """
            ```json
            {"title":"Test","hook":"A hook","summary":"A summary"}
            ```
            """;

        var result = AdventureGenerationService.ParseJsonResponse<AdventurePitch>(json, "pitch");

        Assert.Equal("Test", result.Title);
    }

    [Fact]
    public void ParseJsonResponse_strips_fake_ai_prefix()
    {
        var json = """[Fake AI] {"title":"Test","hook":"A hook","summary":"A summary"}""";

        var result = AdventureGenerationService.ParseJsonResponse<AdventurePitch>(json, "pitch");

        Assert.Equal("Test", result.Title);
    }

    [Fact]
    public void ParseJsonResponse_returns_exception_on_invalid_json()
    {
        var json = "this is not json at all";

        Assert.Throws<InvalidOperationException>(
            () => AdventureGenerationService.ParseJsonResponse<AdventurePitch>(json, "pitch"));
    }

    // ── Fake AI Provider for Tests ────────────────────────────────────────────

    /// <summary>
    /// A fake AI provider that returns structured JSON responses appropriate for each
    /// generation stage, based on the prompt content.
    /// </summary>
    private sealed class StructuredFakeAiProvider : IAiProvider
    {
        public string ProviderName => "StructuredFake";
        public int CallCount { get; private set; }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public Task<string> ChatCompletionAsync(
            IReadOnlyList<AiMessage> messages,
            CancellationToken ct = default)
        {
            CallCount++;
            var userMessage = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

            string response;
            if (userMessage.Contains("Generate an adventure outline", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new AdventureOutline
                {
                    SceneSummaries =
                    [
                        new SceneSummary { Title = "The Village of Ashford", Description = "Players learn of the temple threat", SceneType = "roleplay" },
                        new SceneSummary { Title = "The Forest Path", Description = "Journey through dangerous woods", SceneType = "exploration" },
                        new SceneSummary { Title = "Temple Entrance", Description = "Overcome the temple guardians", SceneType = "combat" },
                        new SceneSummary { Title = "The Inner Sanctum", Description = "Face the necromancer", SceneType = "combat" },
                    ]
                }, JsonOptions);
            }
            else if (userMessage.Contains("Generate an adventure pitch", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new AdventurePitch
                {
                    Title = "The Lost Temple of Shadows",
                    Hook = "Ancient evil stirs beneath a forgotten temple",
                    Summary = "A group of adventurers must delve into a forgotten temple to stop a dark ritual. Along the way they face traps, undead guardians, and a cunning necromancer.",
                }, JsonOptions);
            }
            else if (userMessage.Contains("Generate detailed scenes", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new List<GeneratedScene>
                {
                    new() { SceneId = "scene-1", Title = "The Village of Ashford", Description = "A quiet village threatened by undead", SceneType = "roleplay", ConnectsTo = ["scene-2"], ReadAloudText = "You arrive at a small village...", DmNotes = "The elder knows the temple location" },
                    new() { SceneId = "scene-2", Title = "The Forest Path", Description = "A dangerous journey through dark woods", SceneType = "exploration", ConnectsTo = ["scene-3"], ReadAloudText = "The forest grows darker..." },
                    new() { SceneId = "scene-3", Title = "Temple Entrance", Description = "Undead guard the temple doors", SceneType = "combat", ConnectsTo = ["scene-4"] },
                    new() { SceneId = "scene-4", Title = "The Inner Sanctum", Description = "The necromancer performs a dark ritual", SceneType = "combat", ConnectsTo = [] },
                }, JsonOptions);
            }
            else if (userMessage.Contains("Generate NPCs", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new List<GeneratedNpc>
                {
                    new() { Name = "Elder Moira", Role = "quest giver", Personality = "Wise and worried grandmother figure", StatsSummary = "Commoner", SceneIds = ["scene-1"], Faction = null },
                    new() { Name = "Varkoth the Pale", Role = "villain", Personality = "Cold, calculating, obsessed with immortality", StatsSummary = "CR 5 Necromancer", SceneIds = ["scene-4"], Faction = "Cult of Shadows" },
                    new() { Name = "Finn the Scout", Role = "ally", Personality = "Cheerful but cautious woodsman", StatsSummary = "CR 1/2 Scout", SceneIds = ["scene-2"], Faction = null },
                }, JsonOptions);
            }
            else if (userMessage.Contains("Generate encounters", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new List<GeneratedEncounter>
                {
                    new() { Title = "Temple Guardians", SceneId = "scene-3", Enemies = [new EncounterCreature { Name = "Skeleton", Count = 4, ChallengeRating = "1/4" }, new EncounterCreature { Name = "Zombie", Count = 2, ChallengeRating = "1/4" }], Difficulty = "medium", Tactics = "Skeletons use ranged attacks while zombies engage in melee", Environment = "Crumbling stone steps with pillars for cover" },
                    new() { Title = "The Necromancer's Stand", SceneId = "scene-4", Enemies = [new EncounterCreature { Name = "Necromancer", Count = 1, ChallengeRating = "5" }, new EncounterCreature { Name = "Shadow", Count = 2, ChallengeRating = "1/2" }], Difficulty = "hard", Tactics = "Necromancer stays at range, shadows flank", Environment = "Dark ritual chamber with magical circles" },
                }, JsonOptions);
            }
            else if (userMessage.Contains("Generate treasure", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new AdventureTreasure
                {
                    GoldTotal = 250,
                    Items =
                    [
                        new TreasureItem { Name = "Silver Necklace", Description = "A finely crafted silver necklace", SceneId = "scene-1", Value = 25 },
                        new TreasureItem { Name = "Healing Potion", Description = "Restores 2d4+2 hit points", SceneId = "scene-3", Value = 50 },
                    ],
                    MagicItems =
                    [
                        new TreasureItem { Name = "Cloak of Protection", Description = "+1 to AC and saving throws", SceneId = "scene-4", Value = 0 },
                    ],
                }, JsonOptions);
            }
            else if (userMessage.Contains("Generate clues", StringComparison.OrdinalIgnoreCase))
            {
                response = JsonSerializer.Serialize(new AdventureClues
                {
                    Secrets =
                    [
                        new AdventureSecret { Title = "The Elder's Guilt", Content = "Elder Moira's son joined the cult willingly", SceneId = "scene-1", DiscoveryMethod = "DC 15 Insight check during conversation" },
                        new AdventureSecret { Title = "Hidden Passage", Content = "A secret tunnel bypasses the temple guardians", SceneId = "scene-2", DiscoveryMethod = "DC 14 Perception check near the old oak" },
                    ],
                    Handouts =
                    [
                        new AdventureHandout { Title = "Torn Journal Page", Content = "...the ritual requires three souls bound in shadow...", SceneId = "scene-3" },
                    ],
                    FailForwardPaths =
                    [
                        new FailForwardPath { Trigger = "Players cannot find the temple entrance", Resolution = "Finn the Scout arrives and offers to guide them", SceneId = "scene-2" },
                        new FailForwardPath { Trigger = "Players are defeated by temple guardians", Resolution = "They wake up captured in the inner sanctum, creating a prison escape scenario", SceneId = "scene-3" },
                    ],
                }, JsonOptions);
            }
            else
            {
                // Default fallback - return a pitch
                response = JsonSerializer.Serialize(new AdventurePitch
                {
                    Title = "Default Adventure",
                    Hook = "A default hook",
                    Summary = "A default summary for testing",
                }, JsonOptions);
            }

            return Task.FromResult(response);
        }

        public Task<AiStructuredOutput> StructuredChatCompletionAsync(
            IReadOnlyList<AiMessage> messages,
            CancellationToken ct = default)
        {
            return Task.FromResult(new AiStructuredOutput
            {
                Narration = "Structured output not used in generation",
                PrivateDmNote = null,
                RulesCitations = [],
                ProposedActions = [],
            });
        }
    }
}
