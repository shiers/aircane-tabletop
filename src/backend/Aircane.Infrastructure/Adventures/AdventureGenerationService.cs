using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Implements the staged adventure generation pipeline.
/// Each stage calls the AI provider with a structured prompt and parses the JSON response.
/// Previous stage outputs are passed as context to subsequent stages.
/// </summary>
public sealed class AdventureGenerationService : IAdventureGenerationService
{
    private readonly IAiProvider _aiProvider;
    private readonly AircaneDbContext _db;
    private readonly ILogger<AdventureGenerationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public AdventureGenerationService(
        IAiProvider aiProvider,
        AircaneDbContext db,
        ILogger<AdventureGenerationService> logger)
    {
        _aiProvider = aiProvider;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GeneratedAdventureDto> GenerateAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting adventure generation pipeline. Mode={Mode}, Ruleset={Ruleset}, PartySize={PartySize}",
            request.Mode, request.Ruleset, request.PartySize);

        // Stage 3: Pitch
        var pitch = await RegeneratePitchAsync(request, partyAnalysis, cancellationToken);
        _logger.LogInformation("Pitch generated: {Title}", pitch.Title);

        // Stage 4: Outline
        var outline = await RegenerateOutlineAsync(request, partyAnalysis, pitch, cancellationToken);
        _logger.LogInformation("Outline generated with {Count} scenes", outline.SceneSummaries.Count);

        // Stage 5: Scenes
        var scenes = await RegenerateScenesAsync(request, partyAnalysis, pitch, outline, cancellationToken);
        _logger.LogInformation("Scenes generated: {Count}", scenes.Count);

        // Stage 6: NPCs
        var npcs = await RegenerateNpcsAsync(request, partyAnalysis, pitch, scenes, cancellationToken);
        _logger.LogInformation("NPCs generated: {Count}", npcs.Count);

        // Stage 7: Encounters
        var encounters = await RegenerateEncountersAsync(request, partyAnalysis, pitch, scenes, cancellationToken);
        _logger.LogInformation("Encounters generated: {Count}", encounters.Count);

        // Stage 8: Clues
        var clues = await RegenerateCluesAsync(request, partyAnalysis, pitch, scenes, npcs, cancellationToken);
        _logger.LogInformation("Clues generated: {Secrets} secrets, {Handouts} handouts, {FailForward} fail-forward paths",
            clues.Secrets.Count, clues.Handouts.Count, clues.FailForwardPaths.Count);

        // Stage 9: Treasure
        var treasure = await RegenerateTreasureAsync(request, partyAnalysis, pitch, scenes, encounters, cancellationToken);
        _logger.LogInformation("Treasure generated: {Gold}gp, {Items} items, {Magic} magic items",
            treasure.GoldTotal, treasure.Items.Count, treasure.MagicItems.Count);

        // Persist the generated adventure
        var now = DateTime.UtcNow;
        var entity = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = pitch.Title,
            Status = GeneratedAdventureStatus.Draft,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            PartyAnalysisJson = partyAnalysis != null ? JsonSerializer.Serialize(partyAnalysis, JsonOptions) : null,
            PitchJson = JsonSerializer.Serialize(pitch, JsonOptions),
            OutlineJson = JsonSerializer.Serialize(outline, JsonOptions),
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

        _db.GeneratedAdventures.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Adventure persisted with Id={Id}", entity.Id);

        return new GeneratedAdventureDto
        {
            Id = entity.Id,
            Status = entity.Status.ToString(),
            Request = request,
            PartyAnalysis = partyAnalysis,
            Pitch = pitch,
            Outline = outline,
            Scenes = scenes,
            Npcs = npcs,
            Encounters = encounters,
            Treasure = treasure,
            Clues = clues,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    /// <inheritdoc />
    public async Task<AdventurePitch> RegeneratePitchAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildPitchPrompt(request);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<AdventurePitch>(response, "pitch");
    }

    /// <inheritdoc />
    public async Task<AdventureOutline> RegenerateOutlineAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildOutlinePrompt(request, pitch);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<AdventureOutline>(response, "outline");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedScene>> RegenerateScenesAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        AdventureOutline outline,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildScenesPrompt(request, pitch, outline);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<List<GeneratedScene>>(response, "scenes");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedNpc>> RegenerateNpcsAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildNpcsPrompt(request, pitch, scenes);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<List<GeneratedNpc>>(response, "npcs");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedEncounter>> RegenerateEncountersAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildEncountersPrompt(request, pitch, scenes);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<List<GeneratedEncounter>>(response, "encounters");
    }

    /// <inheritdoc />
    public async Task<AdventureTreasure> RegenerateTreasureAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        IReadOnlyList<GeneratedEncounter> encounters,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildTreasurePrompt(request, pitch, scenes, encounters);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<AdventureTreasure>(response, "treasure");
    }

    /// <inheritdoc />
    public async Task<AdventureClues> RegenerateCluesAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        IReadOnlyList<GeneratedNpc> npcs,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request, partyAnalysis);
        var userPrompt = BuildCluesPrompt(request, pitch, scenes, npcs);

        var response = await CallAiAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseJsonResponse<AdventureClues>(response, "clues");
    }

    // ── AI Call Helper ─────────────────────────────────────────────────────────

    private async Task<string> CallAiAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var messages = new List<AiMessage>
        {
            AiMessage.System(systemPrompt),
            AiMessage.User(userPrompt),
        };

        return await _aiProvider.ChatCompletionAsync(messages, cancellationToken);
    }

    // ── JSON Parsing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a JSON response from the AI, stripping any markdown code fences.
    /// Throws if parsing fails so the caller can handle the error.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the AI response cannot be parsed.</exception>
    public static T ParseJsonResponse<T>(string response, string stageName)
    {
        // Strip markdown code fences if present
        var json = response.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0)
                json = json[(firstNewline + 1)..];
            if (json.EndsWith("```"))
                json = json[..^3];
            json = json.Trim();
        }

        // Strip the [Fake AI] prefix if present (from FakeAiProvider)
        if (json.StartsWith("[Fake AI]"))
        {
            json = json["[Fake AI]".Length..].Trim();
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (result is null)
                throw new InvalidOperationException($"AI returned null for stage '{stageName}'.");
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse AI response for stage '{stageName}': {ex.Message}", ex);
        }
    }

    // ── Prompt Builders ───────────────────────────────────────────────────────

    private static string BuildSystemPrompt(GenerateAdventureRequest request, PartyAnalysisResult? partyAnalysis)
    {
        var prompt = $"""
            You are an expert tabletop RPG adventure designer for {request.Ruleset}.
            You create structured, playable adventures in JSON format.

            Adventure parameters:
            - Mode: {request.Mode}
            - Party size: {request.PartySize}
            - Average level: {request.AverageLevel}
            - Tone: {request.Tone}
            - Length: {request.Length}
            - Difficulty: {request.Difficulty}
            - Combat ratio: {request.CombatRatio}%
            - Exploration ratio: {request.ExplorationRatio}%
            - Roleplay ratio: {request.RoleplayRatio}%
            """;

        if (!string.IsNullOrWhiteSpace(request.Setting))
            prompt += $"\n- Setting: {request.Setting}";

        if (partyAnalysis != null)
        {
            prompt += $"""


                Party analysis:
                - Party size: {partyAnalysis.PartySize}
                - Average level: {partyAnalysis.AverageLevel}
                - Classes: {string.Join(", ", partyAnalysis.Classes.Select(c => $"{c.Key} ({c.Value})"))}
                - Average AC: {partyAnalysis.AverageAC}
                - Average HP: {partyAnalysis.AverageHP}
                - Has healing: {partyAnalysis.HasHealing}
                - Has ranged attacks: {partyAnalysis.HasRangedAttacks}
                - Has magic: {partyAnalysis.HasMagic}
                - Capabilities: {string.Join(", ", partyAnalysis.Capabilities)}
                - Weaknesses: {string.Join(", ", partyAnalysis.Weaknesses)}
                """;
        }

        prompt += "\n\nAlways respond with valid JSON only. No markdown, no explanation, just the JSON object or array.";

        return prompt;
    }

    private static string BuildPitchPrompt(GenerateAdventureRequest request)
    {
        return $"""
            Generate an adventure pitch as a JSON object with these fields:
            - "title": a compelling adventure title
            - "hook": a one-line hook to grab player interest
            - "summary": a 2-4 sentence summary of the adventure premise

            The adventure should be a {request.Length} {request.Tone} adventure for {request.PartySize} level {request.AverageLevel} character(s).
            {(string.IsNullOrWhiteSpace(request.Setting) ? "" : $"Setting: {request.Setting}")}
            """;
    }

    private static string BuildOutlinePrompt(GenerateAdventureRequest request, AdventurePitch pitch)
    {
        var sceneCount = request.Length switch
        {
            "one-shot" => "3-5",
            "short" => "5-7",
            "medium" => "7-10",
            "long" => "10-15",
            _ => "5-8",
        };

        return $"""
            Based on this adventure pitch:
            Title: {pitch.Title}
            Hook: {pitch.Hook}
            Summary: {pitch.Summary}

            Generate an adventure outline as a JSON object with a "sceneSummaries" array.
            Each scene summary should have:
            - "title": scene title
            - "description": brief description of what happens
            - "sceneType": one of "combat", "exploration", "roleplay", "puzzle", "social", "chase"

            Create {sceneCount} scenes. Distribute scene types to match these ratios:
            - Combat: {request.CombatRatio}%
            - Exploration: {request.ExplorationRatio}%
            - Roleplay: {request.RoleplayRatio}%
            """;
    }

    private static string BuildScenesPrompt(
        GenerateAdventureRequest request,
        AdventurePitch pitch,
        AdventureOutline outline)
    {
        var outlineJson = JsonSerializer.Serialize(outline.SceneSummaries, JsonOptions);

        return $"""
            Based on this adventure:
            Title: {pitch.Title}
            Summary: {pitch.Summary}

            Outline:
            {outlineJson}

            Generate detailed scenes as a JSON array. Each scene should have:
            - "sceneId": "scene-1", "scene-2", etc.
            - "title": scene title
            - "description": full narrative description
            - "sceneType": matching the outline
            - "connectsTo": array of scene IDs this scene leads to
            - "readAloudText": text for the DM to read to players (optional)
            - "dmNotes": DM-only notes for running the scene (optional)

            Ensure scenes connect logically and form a playable adventure graph.
            """;
    }

    private static string BuildNpcsPrompt(
        GenerateAdventureRequest request,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes)
    {
        var sceneList = string.Join("\n", scenes.Select(s => $"- {s.SceneId}: {s.Title} ({s.SceneType})"));

        return $"""
            Based on this adventure:
            Title: {pitch.Title}
            Summary: {pitch.Summary}

            Scenes:
            {sceneList}

            Generate NPCs as a JSON array. Each NPC should have:
            - "name": NPC name
            - "role": one of "quest giver", "villain", "ally", "neutral", "merchant", "informant"
            - "personality": brief personality description (1-2 sentences)
            - "statsSummary": brief stat reference (e.g., "CR 2 Bandit Captain", "Commoner")
            - "sceneIds": array of scene IDs where this NPC appears
            - "faction": optional faction name (null if none)

            Create appropriate NPCs for a {request.Tone} {request.Mode} adventure.
            Include at least one quest giver and one antagonist.
            """;
    }

    private static string BuildEncountersPrompt(
        GenerateAdventureRequest request,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes)
    {
        var combatScenes = scenes.Where(s =>
            s.SceneType.Equals("combat", StringComparison.OrdinalIgnoreCase)).ToList();
        var sceneList = string.Join("\n", combatScenes.Select(s => $"- {s.SceneId}: {s.Title}"));

        return $"""
            Based on this adventure:
            Title: {pitch.Title}
            Party: {request.PartySize} level {request.AverageLevel} character(s)
            Difficulty: {request.Difficulty}

            Combat scenes:
            {sceneList}

            Generate encounters as a JSON array. Each encounter should have:
            - "title": encounter title
            - "sceneId": which scene this encounter belongs to
            - "enemies": array of objects with "name", "count" (integer), "challengeRating"
            - "difficulty": "{request.Difficulty}"
            - "tactics": suggested enemy tactics (1-2 sentences)
            - "environment": terrain or environmental features (optional)

            Scale encounters for {request.PartySize} level {request.AverageLevel} characters at {request.Difficulty} difficulty.
            """;
    }

    private static string BuildTreasurePrompt(
        GenerateAdventureRequest request,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        IReadOnlyList<GeneratedEncounter> encounters)
    {
        var sceneIds = string.Join(", ", scenes.Select(s => s.SceneId));

        return $"""
            Based on this adventure:
            Title: {pitch.Title}
            Party: {request.PartySize} level {request.AverageLevel} character(s)
            Scenes: {sceneIds}

            Generate treasure as a JSON object with:
            - "goldTotal": total gold pieces appropriate for level {request.AverageLevel} characters
            - "items": array of mundane items, each with "name", "description", "sceneId", "value"
            - "magicItems": array of magic items, each with "name", "description", "sceneId", "value"

            Distribute treasure across scenes. Include 1-3 magic items appropriate for level {request.AverageLevel}.
            """;
    }

    private static string BuildCluesPrompt(
        GenerateAdventureRequest request,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        IReadOnlyList<GeneratedNpc> npcs)
    {
        var sceneList = string.Join("\n", scenes.Select(s => $"- {s.SceneId}: {s.Title}"));
        var npcList = string.Join("\n", npcs.Select(n => $"- {n.Name} ({n.Role})"));

        return $"""
            Based on this adventure:
            Title: {pitch.Title}
            Summary: {pitch.Summary}

            Scenes:
            {sceneList}

            NPCs:
            {npcList}

            Generate clues, secrets, and fail-forward paths as a JSON object with:
            - "secrets": array of objects with "title", "content", "sceneId", "discoveryMethod"
            - "handouts": array of objects with "title", "content", "sceneId"
            - "failForwardPaths": array of objects with "trigger", "resolution", "sceneId"

            Include at least 2 secrets, 1 handout, and 2 fail-forward paths.
            Fail-forward paths should ensure players can never get permanently stuck.
            """;
    }
}
