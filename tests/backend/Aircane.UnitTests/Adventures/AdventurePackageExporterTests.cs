using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Adventures;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class AdventurePackageExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static GeneratedAdventureDto CreateTestAdventure()
    {
        return new GeneratedAdventureDto
        {
            Id = Guid.NewGuid(),
            Status = "Draft",
            Request = new GenerateAdventureRequest
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
            },
            Pitch = new AdventurePitch
            {
                Title = "The Lost Temple",
                Hook = "Ancient evil stirs",
                Summary = "A group must stop a dark ritual.",
            },
            Outline = new AdventureOutline
            {
                SceneSummaries =
                [
                    new SceneSummary { Title = "Village", Description = "Starting point", SceneType = "roleplay" },
                    new SceneSummary { Title = "Temple", Description = "Final battle", SceneType = "combat" },
                ]
            },
            Scenes =
            [
                new GeneratedScene { SceneId = "scene-1", Title = "Village", Description = "A quiet village", SceneType = "roleplay", ConnectsTo = ["scene-2"] },
                new GeneratedScene { SceneId = "scene-2", Title = "Temple", Description = "The dark temple", SceneType = "combat", ConnectsTo = [] },
            ],
            Npcs =
            [
                new GeneratedNpc { Name = "Elder Moira", Role = "quest giver", Personality = "Wise", StatsSummary = "Commoner", SceneIds = ["scene-1"] },
            ],
            Encounters =
            [
                new GeneratedEncounter { Title = "Temple Guards", SceneId = "scene-2", Enemies = [new EncounterCreature { Name = "Skeleton", Count = 4, ChallengeRating = "1/4" }], Difficulty = "medium", Tactics = "Attack in groups" },
            ],
            Treasure = new AdventureTreasure
            {
                GoldTotal = 200,
                Items = [new TreasureItem { Name = "Potion", Description = "Healing potion", SceneId = "scene-2", Value = 50 }],
                MagicItems = [],
            },
            Clues = new AdventureClues
            {
                Secrets = [new AdventureSecret { Title = "Hidden Path", Content = "A secret tunnel", SceneId = "scene-1", DiscoveryMethod = "DC 14 Perception" }],
                Handouts = [],
                FailForwardPaths = [new FailForwardPath { Trigger = "Players stuck", Resolution = "NPC arrives", SceneId = "scene-1" }],
            },
            CreatedAt = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Utc),
        };
    }

    // ── ExportToPackage Tests ─────────────────────────────────────────────────

    [Fact]
    public void ExportToPackage_produces_all_expected_files()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);

        Assert.Contains("adventure.json", package.Keys);
        Assert.Contains("scenes.json", package.Keys);
        Assert.Contains("npcs.json", package.Keys);
        Assert.Contains("encounters.json", package.Keys);
        Assert.Contains("treasure.json", package.Keys);
        Assert.Contains("clues.json", package.Keys);
        Assert.Contains("session-notes.md", package.Keys);
    }

    [Fact]
    public void ExportToPackage_adventure_json_contains_metadata()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);
        var metadata = JsonSerializer.Deserialize<AdventurePackageMetadata>(
            package["adventure.json"], JsonOptions);

        Assert.NotNull(metadata);
        Assert.Equal(adventure.Id, metadata.Id);
        Assert.Equal("The Lost Temple", metadata.Title);
        Assert.Equal("Draft", metadata.Status);
        Assert.Equal("D&D 5e 2014", metadata.Ruleset);
        Assert.Equal("D&D 5e 2014", metadata.GameSystem);
        Assert.NotNull(metadata.Pitch);
        Assert.NotNull(metadata.Outline);
    }

    [Fact]
    public void ExportToPackage_scenes_json_contains_scene_graph()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);
        var scenes = JsonSerializer.Deserialize<List<GeneratedScene>>(
            package["scenes.json"], JsonOptions);

        Assert.NotNull(scenes);
        Assert.Equal(2, scenes.Count);
        Assert.Equal("scene-1", scenes[0].SceneId);
        Assert.Contains("scene-2", scenes[0].ConnectsTo);
    }

    [Fact]
    public void ExportToPackage_npcs_json_contains_npcs()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);
        var npcs = JsonSerializer.Deserialize<List<GeneratedNpc>>(
            package["npcs.json"], JsonOptions);

        Assert.NotNull(npcs);
        Assert.Single(npcs);
        Assert.Equal("Elder Moira", npcs[0].Name);
    }

    [Fact]
    public void ExportToPackage_encounters_json_contains_encounters()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);
        var encounters = JsonSerializer.Deserialize<List<GeneratedEncounter>>(
            package["encounters.json"], JsonOptions);

        Assert.NotNull(encounters);
        Assert.Single(encounters);
        Assert.Equal("Temple Guards", encounters[0].Title);
    }

    [Fact]
    public void ExportToPackage_treasure_json_contains_treasure()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);
        var treasure = JsonSerializer.Deserialize<AdventureTreasure>(
            package["treasure.json"], JsonOptions);

        Assert.NotNull(treasure);
        Assert.Equal(200, treasure.GoldTotal);
    }

    [Fact]
    public void ExportToPackage_clues_json_contains_clues()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);
        var clues = JsonSerializer.Deserialize<AdventureClues>(
            package["clues.json"], JsonOptions);

        Assert.NotNull(clues);
        Assert.Single(clues.Secrets);
        Assert.Single(clues.FailForwardPaths);
    }

    [Fact]
    public void ExportToPackage_session_notes_contains_adventure_title()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);

        Assert.Contains("The Lost Temple", package["session-notes.md"]);
    }

    [Fact]
    public void ExportToPackage_session_notes_contains_scene_titles()
    {
        var adventure = CreateTestAdventure();

        var package = AdventurePackageExporter.ExportToPackage(adventure);

        Assert.Contains("Village", package["session-notes.md"]);
        Assert.Contains("Temple", package["session-notes.md"]);
    }

    [Fact]
    public void ExportToPackage_omits_null_sections()
    {
        var adventure = new GeneratedAdventureDto
        {
            Id = Guid.NewGuid(),
            Status = "Draft",
            Request = new GenerateAdventureRequest
            {
                Mode = "Solo",
                Ruleset = "D&D 5e 2014",
                GameSystem = "D&D 5e 2014",
                Tone = "dark",
                Length = "short",
                Difficulty = "hard",
                CombatRatio = 50,
                ExplorationRatio = 25,
                RoleplayRatio = 25,
            },
            Pitch = new AdventurePitch { Title = "Partial", Hook = "Hook", Summary = "Summary" },
            Scenes = null,
            Npcs = null,
            Encounters = null,
            Treasure = null,
            Clues = null,
        };

        var package = AdventurePackageExporter.ExportToPackage(adventure);

        Assert.Contains("adventure.json", package.Keys);
        Assert.Contains("session-notes.md", package.Keys);
        Assert.DoesNotContain("scenes.json", package.Keys);
        Assert.DoesNotContain("npcs.json", package.Keys);
        Assert.DoesNotContain("encounters.json", package.Keys);
        Assert.DoesNotContain("treasure.json", package.Keys);
        Assert.DoesNotContain("clues.json", package.Keys);
    }

    [Fact]
    public void ExportToPackage_throws_on_null_adventure()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AdventurePackageExporter.ExportToPackage(null!));
    }

    // ── ExportToDirectoryAsync Tests ──────────────────────────────────────────

    [Fact]
    public async Task ExportToDirectoryAsync_creates_directory_and_files()
    {
        var adventure = CreateTestAdventure();
        var tempDir = Path.Combine(Path.GetTempPath(), "aircane-test-" + Guid.NewGuid());

        try
        {
            var resultPath = await AdventurePackageExporter.ExportToDirectoryAsync(adventure, tempDir);

            Assert.True(Directory.Exists(resultPath));
            Assert.True(File.Exists(Path.Combine(resultPath, "adventure.json")));
            Assert.True(File.Exists(Path.Combine(resultPath, "scenes.json")));
            Assert.True(File.Exists(Path.Combine(resultPath, "npcs.json")));
            Assert.True(File.Exists(Path.Combine(resultPath, "encounters.json")));
            Assert.True(File.Exists(Path.Combine(resultPath, "treasure.json")));
            Assert.True(File.Exists(Path.Combine(resultPath, "clues.json")));
            Assert.True(File.Exists(Path.Combine(resultPath, "session-notes.md")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExportToDirectoryAsync_uses_adventure_id_as_folder_name()
    {
        var adventure = CreateTestAdventure();
        var tempDir = Path.Combine(Path.GetTempPath(), "aircane-test-" + Guid.NewGuid());

        try
        {
            var resultPath = await AdventurePackageExporter.ExportToDirectoryAsync(adventure, tempDir);

            Assert.EndsWith(adventure.Id.ToString(), resultPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
