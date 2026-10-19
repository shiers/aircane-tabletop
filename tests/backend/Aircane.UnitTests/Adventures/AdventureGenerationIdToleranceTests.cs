using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>
/// Stages that reference an earlier stage's scene id must tolerate the model returning the id as a
/// number, map it to the real scene id, reject ids that can't be mapped, and retry once on failure.
/// </summary>
public sealed class AdventureGenerationIdToleranceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly ScriptedAiProvider _ai = new();
    private readonly AdventureGenerationService _service;

    public AdventureGenerationIdToleranceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
        _service = new AdventureGenerationService(_ai, _db, NullLogger<AdventureGenerationService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static readonly GenerateAdventureRequest Request = new()
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

    private static readonly AdventurePitch Pitch = new() { Title = "T", Hook = "H", Summary = "S" };

    private static GeneratedScene Scene(string id, string type = "combat", params string[] connectsTo) => new()
    {
        SceneId = id,
        Title = $"Title {id}",
        Description = "d",
        SceneType = type,
        ConnectsTo = connectsTo,
    };

    private static readonly IReadOnlyList<GeneratedScene> Scenes =
    [
        Scene("scene-1", "roleplay", "scene-2"),
        Scene("scene-2", "combat", "scene-3"),
        Scene("scene-3", "combat"),
    ];

    private static string EncountersJson(string sceneIdJson) => $$"""
        [{"title":"Ambush","sceneId":{{sceneIdJson}},"enemies":[{"name":"Goblin","count":3,"challengeRating":"1/4"}],"difficulty":"medium","tactics":"Flank"}]
        """;

    private Task<IReadOnlyList<GeneratedEncounter>> Encounters()
        => _service.RegenerateEncountersAsync(Request, null, Pitch, Scenes);

    // ── Reproduction of the reported failure ──────────────────────────────────

    [Fact]
    public async Task Encounters_numeric_sceneId_does_not_fail_the_stage()
    {
        _ai.Enqueue(EncountersJson("2"));

        var encounters = await Encounters();

        Assert.Equal("scene-2", Assert.Single(encounters).SceneId);
    }

    [Fact]
    public void Raw_parse_keeps_a_numeric_sceneId_as_text_instead_of_failing()
    {
        var encounters = AdventureGenerationService.ParseJsonResponse<List<GeneratedEncounter>>(EncountersJson("2"), "encounters");

        Assert.Equal("2", Assert.Single(encounters).SceneId);
    }

    // ── Encounters: numeric, string, null, unknown ────────────────────────────

    [Fact]
    public async Task Encounters_string_sceneId_is_kept()
    {
        _ai.Enqueue(EncountersJson("\"scene-3\""));

        Assert.Equal("scene-3", Assert.Single(await Encounters()).SceneId);
        Assert.Equal(1, _ai.CallCount);
    }

    [Fact]
    public async Task Encounters_numeric_sceneId_is_treated_as_one_based_index()
    {
        _ai.Enqueue(EncountersJson("3"));

        Assert.Equal("scene-3", Assert.Single(await Encounters()).SceneId);
    }

    [Fact]
    public async Task Encounters_numeric_sceneIds_are_zero_based_when_the_response_uses_zero()
    {
        _ai.Enqueue("""
            [{"title":"A","sceneId":0,"enemies":[],"difficulty":"medium","tactics":"t"},
             {"title":"B","sceneId":2,"enemies":[],"difficulty":"medium","tactics":"t"}]
            """);

        var encounters = await Encounters();

        Assert.Equal(["scene-1", "scene-3"], encounters.Select(e => e.SceneId));
    }

    [Fact]
    public async Task Encounters_numeric_string_sceneId_is_mapped()
    {
        _ai.Enqueue(EncountersJson("\"2\""));

        Assert.Equal("scene-2", Assert.Single(await Encounters()).SceneId);
    }

    [Fact]
    public async Task Encounters_null_sceneId_fails_naming_the_field()
    {
        _ai.Enqueue(EncountersJson("null"));
        _ai.Enqueue(EncountersJson("null"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Encounters);

        Assert.Contains("encounters[0].sceneId", ex.Message);
        Assert.Contains("null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Encounters_unknown_sceneId_fails_naming_the_field_and_value()
    {
        _ai.Enqueue(EncountersJson("\"scene-99\""));
        _ai.Enqueue(EncountersJson("\"scene-99\""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Encounters);

        Assert.Contains("encounters[0].sceneId", ex.Message);
        Assert.Contains("scene-99", ex.Message);
    }

    [Fact]
    public async Task Encounters_out_of_range_numeric_sceneId_fails_naming_the_value()
    {
        _ai.Enqueue(EncountersJson("17"));
        _ai.Enqueue(EncountersJson("17"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Encounters);

        Assert.Contains("encounters[0].sceneId", ex.Message);
        Assert.Contains("17", ex.Message);
    }

    // ── Retry ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stage_retries_once_feeding_the_error_back_and_succeeds()
    {
        _ai.Enqueue(EncountersJson("\"scene-99\""));
        _ai.Enqueue(EncountersJson("\"scene-2\""));

        var encounters = await Encounters();

        Assert.Equal("scene-2", Assert.Single(encounters).SceneId);
        Assert.Equal(2, _ai.CallCount);

        var retry = _ai.Calls[1];
        Assert.Equal(["system", "user", "assistant", "user"], retry.Select(m => m.Role));
        Assert.Equal(EncountersJson("\"scene-99\""), retry[2].Content);
        Assert.Contains("scene-99", retry[3].Content);
        Assert.Contains("encounters[0].sceneId", retry[3].Content);
    }

    [Fact]
    public async Task Stage_retries_once_after_malformed_json()
    {
        _ai.Enqueue("not json at all");
        _ai.Enqueue(EncountersJson("\"scene-2\""));

        Assert.Equal("scene-2", Assert.Single(await Encounters()).SceneId);
        Assert.Equal(2, _ai.CallCount);
    }

    [Fact]
    public async Task Stage_gives_a_clear_error_after_two_failures_and_does_not_retry_again()
    {
        _ai.Enqueue(EncountersJson("\"scene-99\""));
        _ai.Enqueue(EncountersJson("\"scene-98\""));
        _ai.Enqueue(EncountersJson("\"scene-2\""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Encounters);

        Assert.Equal(2, _ai.CallCount);
        Assert.Contains("encounters", ex.Message);
        Assert.Contains("2 attempts", ex.Message);
        Assert.Contains("scene-98", ex.Message); // the last error, not the first
    }

    [Fact]
    public async Task Stage_does_not_retry_when_the_first_attempt_is_valid()
    {
        _ai.Enqueue(EncountersJson("\"scene-2\""));

        await Encounters();

        Assert.Equal(1, _ai.CallCount);
    }

    // ── Prompts ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Encounters_prompt_lists_the_valid_scene_ids_and_demands_exact_string_ids()
    {
        _ai.Enqueue(EncountersJson("\"scene-2\""));

        await Encounters();

        var prompt = _ai.Calls[0].Last(m => m.Role == "user").Content;
        Assert.Contains("scene-1, scene-2, scene-3", prompt);
        Assert.Contains("exact string", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_id_referencing_stage_prompt_demands_exact_string_ids()
    {
        _ai.Enqueue("""[{"name":"N","role":"ally","personality":"p","statsSummary":"s","sceneIds":["scene-1"]}]""");
        await _service.RegenerateNpcsAsync(Request, null, Pitch, Scenes);

        _ai.Enqueue("""{"secrets":[],"handouts":[],"failForwardPaths":[]}""");
        await _service.RegenerateCluesAsync(Request, null, Pitch, Scenes, []);

        _ai.Enqueue("""{"goldTotal":1,"items":[],"magicItems":[]}""");
        await _service.RegenerateTreasureAsync(Request, null, Pitch, Scenes, []);

        _ai.Enqueue("""[{"sceneId":"scene-1","title":"t","description":"d","sceneType":"combat","connectsTo":[]}]""");
        await _service.RegenerateScenesAsync(Request, null, Pitch,
            new AdventureOutline { SceneSummaries = [new SceneSummary { Title = "t", Description = "d", SceneType = "combat" }] });

        foreach (var call in _ai.Calls)
        {
            var prompt = call.Last(m => m.Role == "user").Content;
            Assert.Contains("exact string", prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── NPCs ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Npcs_numeric_scene_ids_are_mapped_and_unknown_ones_fail()
    {
        _ai.Enqueue("""[{"name":"N","role":"ally","personality":"p","statsSummary":"s","sceneIds":[1,"scene-3"]}]""");

        var npcs = await _service.RegenerateNpcsAsync(Request, null, Pitch, Scenes);

        Assert.Equal(["scene-1", "scene-3"], Assert.Single(npcs).SceneIds);

        _ai.Enqueue("""[{"name":"N","role":"ally","personality":"p","statsSummary":"s","sceneIds":["nowhere"]}]""");
        _ai.Enqueue("""[{"name":"N","role":"ally","personality":"p","statsSummary":"s","sceneIds":["nowhere"]}]""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateNpcsAsync(Request, null, Pitch, Scenes));
        Assert.Contains("npcs[0].sceneIds[0]", ex.Message);
        Assert.Contains("nowhere", ex.Message);
    }

    // ── Treasure ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Treasure_numeric_scene_ids_are_mapped_and_unknown_ones_fail()
    {
        _ai.Enqueue("""
            {"goldTotal":10,
             "items":[{"name":"Ring","description":"d","sceneId":2,"value":5}],
             "magicItems":[{"name":"Sword","description":"d","sceneId":"scene-3","value":0}]}
            """);

        var treasure = await _service.RegenerateTreasureAsync(Request, null, Pitch, Scenes, []);

        Assert.Equal("scene-2", Assert.Single(treasure.Items).SceneId);
        Assert.Equal("scene-3", Assert.Single(treasure.MagicItems).SceneId);

        var bad = """{"goldTotal":10,"items":[],"magicItems":[{"name":"Sword","description":"d","sceneId":null,"value":0}]}""";
        _ai.Enqueue(bad);
        _ai.Enqueue(bad);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateTreasureAsync(Request, null, Pitch, Scenes, []));
        Assert.Contains("treasure.magicItems[0].sceneId", ex.Message);
    }

    // ── Clues ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clues_numeric_scene_ids_are_mapped_in_every_list_and_unknown_ones_fail()
    {
        _ai.Enqueue("""
            {"secrets":[{"title":"s","content":"c","sceneId":1,"discoveryMethod":"d"}],
             "handouts":[{"title":"h","content":"c","sceneId":2}],
             "failForwardPaths":[{"trigger":"t","resolution":"r","sceneId":3}]}
            """);

        var clues = await _service.RegenerateCluesAsync(Request, null, Pitch, Scenes, []);

        Assert.Equal("scene-1", Assert.Single(clues.Secrets).SceneId);
        Assert.Equal("scene-2", Assert.Single(clues.Handouts).SceneId);
        Assert.Equal("scene-3", Assert.Single(clues.FailForwardPaths).SceneId);

        var bad = """{"secrets":[],"handouts":[],"failForwardPaths":[{"trigger":"t","resolution":"r","sceneId":"scene-42"}]}""";
        _ai.Enqueue(bad);
        _ai.Enqueue(bad);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateCluesAsync(Request, null, Pitch, Scenes, []));
        Assert.Contains("clues.failForwardPaths[0].sceneId", ex.Message);
        Assert.Contains("scene-42", ex.Message);
    }

    // ── Scenes (ids defined here; connectsTo references them) ────────────────

    private static readonly AdventureOutline TwoSceneOutline = new()
    {
        SceneSummaries =
        [
            new SceneSummary { Title = "a", Description = "d", SceneType = "combat" },
            new SceneSummary { Title = "b", Description = "d", SceneType = "combat" },
        ],
    };

    [Fact]
    public async Task Scenes_numeric_ids_are_canonicalised_and_connectsTo_follows()
    {
        _ai.Enqueue("""
            [{"sceneId":1,"title":"a","description":"d","sceneType":"combat","connectsTo":[2]},
             {"sceneId":2,"title":"b","description":"d","sceneType":"combat","connectsTo":[]}]
            """);

        var scenes = await _service.RegenerateScenesAsync(Request, null, Pitch, TwoSceneOutline);

        Assert.Equal(["scene-1", "scene-2"], scenes.Select(s => s.SceneId));
        Assert.Equal(["scene-2"], scenes[0].ConnectsTo);
    }

    [Fact]
    public async Task Scenes_duplicate_or_missing_ids_fail_naming_the_field()
    {
        var dup = """
            [{"sceneId":"scene-1","title":"a","description":"d","sceneType":"combat","connectsTo":[]},
             {"sceneId":"scene-1","title":"b","description":"d","sceneType":"combat","connectsTo":[]}]
            """;
        _ai.Enqueue(dup);
        _ai.Enqueue(dup);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateScenesAsync(Request, null, Pitch, TwoSceneOutline));
        Assert.Contains("scenes[1].sceneId", ex.Message);
        Assert.Contains("scene-1", ex.Message);
    }

    [Fact]
    public async Task Scenes_numeric_connectsTo_that_matches_no_scene_fails()
    {
        var bad = """
            [{"sceneId":"scene-1","title":"a","description":"d","sceneType":"combat","connectsTo":[9]},
             {"sceneId":"scene-2","title":"b","description":"d","sceneType":"combat","connectsTo":[]}]
            """;
        _ai.Enqueue(bad);
        _ai.Enqueue(bad);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateScenesAsync(Request, null, Pitch, TwoSceneOutline));
        Assert.Contains("scenes[0].connectsTo[0]", ex.Message);
    }

    // ── Scripted provider ─────────────────────────────────────────────────────

    private sealed class ScriptedAiProvider : IAiProvider
    {
        private readonly Queue<string> _responses = new();

        public string ProviderName => "Scripted";
        public List<IReadOnlyList<AiMessage>> Calls { get; } = [];
        public int CallCount => Calls.Count;

        public void Enqueue(string response) => _responses.Enqueue(response);

        public Task<string> ChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        {
            Calls.Add(messages.ToList());
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<AiStructuredOutput> StructuredChatCompletionAsync(
            IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
