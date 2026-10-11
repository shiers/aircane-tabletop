using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;

namespace Aircane.UnitTests.TestDoubles;

/// <summary>
/// Builds a small, fully-populated three-scene adventure for the solo-execution tests.
/// <code>
/// scene-1 "The Sunken Library" -> scene-2, scene-3
/// scene-2 "The Grand Hall"     -> scene-3
/// scene-3 "The Hidden Passage" (final)
/// </code>
/// Content IDs (derived): npc-guard-captain, npc-archivist, clue-1, secret-1, encounter-1.
/// </summary>
internal static class AdventureSessionTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static GeneratedAdventure CreateAdventure(
        GeneratedAdventureStatus status = GeneratedAdventureStatus.Approved,
        IReadOnlyList<GeneratedScene>? scenesOverride = null)
    {
        var request = new GenerateAdventureRequest
        {
            Mode = "Solo",
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            PartySize = 1,
            AverageLevel = 3,
            Tone = "gritty",
            Length = "short",
            Difficulty = "medium",
            CombatRatio = 30,
            ExplorationRatio = 40,
            RoleplayRatio = 30,
        };

        var pitch = new AdventurePitch { Title = "The Sunken Archive", Hook = "A tome is missing", Summary = "Recover it." };

        var scenes = scenesOverride ??
        [
            new GeneratedScene
            {
                SceneId = "scene-1",
                Title = "The Sunken Library",
                Description = "Flooded shelves rise out of black water.",
                SceneType = "exploration",
                ConnectsTo = ["scene-2", "scene-3"],
                Objectives = ["Find the missing tome", "Deal with the guard"],
                ReadAloudText = "Cold water laps at your boots.",
                DmNotes = "The tome is behind the third shelf.",
            },
            new GeneratedScene
            {
                SceneId = "scene-2",
                Title = "The Grand Hall",
                Description = "A vaulted hall of cracked marble.",
                SceneType = "combat",
                ConnectsTo = ["scene-3"],
            },
            new GeneratedScene
            {
                SceneId = "scene-3",
                Title = "The Hidden Passage",
                Description = "A narrow tunnel behind the wall.",
                SceneType = "roleplay",
                ConnectsTo = [],
            },
        ];

        var npcs = new List<GeneratedNpc>
        {
            new() { Name = "Guard Captain", Role = "villain", Personality = "Gruff", StatsSummary = "CR 2", SceneIds = ["scene-1"] },
            new() { Name = "Archivist", Role = "ally", Personality = "Timid", StatsSummary = "Commoner", SceneIds = ["scene-1", "scene-2"] },
        };

        var encounters = new List<GeneratedEncounter>
        {
            new()
            {
                Title = "Hall Ambush",
                SceneId = "scene-2",
                Enemies = [new EncounterCreature { Name = "Skeleton", Count = 3, ChallengeRating = "1/4" }],
                Difficulty = "medium",
                Tactics = "Swarm the weakest.",
            },
        };

        var clues = new AdventureClues
        {
            Secrets = [new AdventureSecret { Title = "The Vault Key", Content = "The key is in the fountain.", SceneId = "scene-2", DiscoveryMethod = "Investigation DC 12" }],
            Handouts = [new AdventureHandout { Title = "The torn letter", Content = "Meet me below the east stair.", SceneId = "scene-1" }],
            FailForwardPaths = [new FailForwardPath { Trigger = "Player cannot find the tome", Resolution = "The Archivist offers a hint", SceneId = "scene-1" }],
        };

        var treasure = new AdventureTreasure { GoldTotal = 100, Items = [], MagicItems = [] };
        var now = DateTime.UtcNow;

        return new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = pitch.Title,
            Status = status,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            PitchJson = JsonSerializer.Serialize(pitch, JsonOptions),
            OutlineJson = JsonSerializer.Serialize(new AdventureOutline { SceneSummaries = [] }, JsonOptions),
            ScenesJson = JsonSerializer.Serialize(scenes, JsonOptions),
            NpcsJson = JsonSerializer.Serialize(npcs, JsonOptions),
            EncountersJson = JsonSerializer.Serialize(encounters, JsonOptions),
            TreasureJson = JsonSerializer.Serialize(treasure, JsonOptions),
            CluesJson = JsonSerializer.Serialize(clues, JsonOptions),
            Ruleset = request.Ruleset,
            GameSystem = request.GameSystem,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
