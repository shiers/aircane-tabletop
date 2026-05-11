namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Complete generated adventure content produced by the staged generation pipeline.
/// Each property corresponds to a generation stage output.
/// </summary>
public sealed record GeneratedAdventureDto
{
    /// <summary>Unique identifier for this generated adventure.</summary>
    public required Guid Id { get; init; }

    /// <summary>Current status of the generation pipeline.</summary>
    public required string Status { get; init; }

    /// <summary>The original generation request parameters.</summary>
    public required GenerateAdventureRequest Request { get; init; }

    /// <summary>Party analysis result (null if no characters were provided).</summary>
    public PartyAnalysisResult? PartyAnalysis { get; init; }

    /// <summary>Adventure pitch: title, hook, and summary.</summary>
    public AdventurePitch? Pitch { get; init; }

    /// <summary>High-level outline of scenes.</summary>
    public AdventureOutline? Outline { get; init; }

    /// <summary>Detailed scene descriptions with connections.</summary>
    public IReadOnlyList<GeneratedScene>? Scenes { get; init; }

    /// <summary>NPCs generated for the adventure.</summary>
    public IReadOnlyList<GeneratedNpc>? Npcs { get; init; }

    /// <summary>Encounters generated for the adventure.</summary>
    public IReadOnlyList<GeneratedEncounter>? Encounters { get; init; }

    /// <summary>Treasure and rewards for the adventure.</summary>
    public AdventureTreasure? Treasure { get; init; }

    /// <summary>Clues, secrets, and fail-forward paths.</summary>
    public AdventureClues? Clues { get; init; }

    /// <summary>When the adventure was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the adventure was last updated.</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Adventure pitch produced by the pitch generation stage.
/// </summary>
public sealed record AdventurePitch
{
    /// <summary>Adventure title.</summary>
    public required string Title { get; init; }

    /// <summary>One-line hook to grab player interest.</summary>
    public required string Hook { get; init; }

    /// <summary>Brief summary of the adventure premise (2-4 sentences).</summary>
    public required string Summary { get; init; }
}

/// <summary>
/// High-level adventure outline with scene summaries.
/// </summary>
public sealed record AdventureOutline
{
    /// <summary>Ordered list of scene summaries forming the adventure structure.</summary>
    public required IReadOnlyList<SceneSummary> SceneSummaries { get; init; }
}

/// <summary>
/// Brief summary of a scene used in the outline stage.
/// </summary>
public sealed record SceneSummary
{
    /// <summary>Scene title or label.</summary>
    public required string Title { get; init; }

    /// <summary>Brief description of what happens in this scene.</summary>
    public required string Description { get; init; }

    /// <summary>Scene type (e.g., "combat", "exploration", "roleplay", "puzzle").</summary>
    public required string SceneType { get; init; }
}

/// <summary>
/// Detailed scene description with connections to other scenes.
/// </summary>
public sealed record GeneratedScene
{
    /// <summary>Scene identifier (matches outline order).</summary>
    public required string SceneId { get; init; }

    /// <summary>Scene title.</summary>
    public required string Title { get; init; }

    /// <summary>Full narrative description of the scene.</summary>
    public required string Description { get; init; }

    /// <summary>Scene type (combat, exploration, roleplay, puzzle, etc.).</summary>
    public required string SceneType { get; init; }

    /// <summary>IDs of scenes that can be reached from this scene.</summary>
    public required IReadOnlyList<string> ConnectsTo { get; init; }

    /// <summary>Read-aloud text for the DM to present to players.</summary>
    public string? ReadAloudText { get; init; }

    /// <summary>DM-only notes for running this scene.</summary>
    public string? DmNotes { get; init; }
}

/// <summary>
/// A generated NPC for the adventure.
/// </summary>
public sealed record GeneratedNpc
{
    /// <summary>NPC name.</summary>
    public required string Name { get; init; }

    /// <summary>Role in the adventure (e.g., "quest giver", "villain", "ally", "neutral").</summary>
    public required string Role { get; init; }

    /// <summary>Brief personality description.</summary>
    public required string Personality { get; init; }

    /// <summary>Brief stat summary (e.g., "CR 2 Bandit Captain" or "Commoner").</summary>
    public required string StatsSummary { get; init; }

    /// <summary>Which scene(s) this NPC appears in.</summary>
    public required IReadOnlyList<string> SceneIds { get; init; }

    /// <summary>Optional faction affiliation.</summary>
    public string? Faction { get; init; }
}

/// <summary>
/// A generated encounter for the adventure.
/// </summary>
public sealed record GeneratedEncounter
{
    /// <summary>Encounter title or label.</summary>
    public required string Title { get; init; }

    /// <summary>Scene this encounter belongs to.</summary>
    public required string SceneId { get; init; }

    /// <summary>List of enemies/creatures in the encounter.</summary>
    public required IReadOnlyList<EncounterCreature> Enemies { get; init; }

    /// <summary>Estimated difficulty (easy, medium, hard, deadly).</summary>
    public required string Difficulty { get; init; }

    /// <summary>Suggested tactics for the enemies.</summary>
    public required string Tactics { get; init; }

    /// <summary>Environmental features or terrain notes.</summary>
    public string? Environment { get; init; }
}

/// <summary>
/// A creature in a generated encounter.
/// </summary>
public sealed record EncounterCreature
{
    /// <summary>Creature name.</summary>
    public required string Name { get; init; }

    /// <summary>Number of this creature in the encounter.</summary>
    public required int Count { get; init; }

    /// <summary>Challenge Rating or level.</summary>
    public required string ChallengeRating { get; init; }
}

/// <summary>
/// Treasure and rewards for the adventure.
/// </summary>
public sealed record AdventureTreasure
{
    /// <summary>Gold pieces awarded across the adventure.</summary>
    public required int GoldTotal { get; init; }

    /// <summary>Mundane items found.</summary>
    public required IReadOnlyList<TreasureItem> Items { get; init; }

    /// <summary>Magic items found.</summary>
    public required IReadOnlyList<TreasureItem> MagicItems { get; init; }
}

/// <summary>
/// A treasure item (mundane or magical).
/// </summary>
public sealed record TreasureItem
{
    /// <summary>Item name.</summary>
    public required string Name { get; init; }

    /// <summary>Brief description.</summary>
    public required string Description { get; init; }

    /// <summary>Which scene this item is found in.</summary>
    public required string SceneId { get; init; }

    /// <summary>Estimated value in gold pieces (0 if priceless or unknown).</summary>
    public int Value { get; init; }
}

/// <summary>
/// Clues, secrets, and fail-forward paths for the adventure.
/// </summary>
public sealed record AdventureClues
{
    /// <summary>Secrets the players can discover.</summary>
    public required IReadOnlyList<AdventureSecret> Secrets { get; init; }

    /// <summary>Handouts or documents the players can find.</summary>
    public required IReadOnlyList<AdventureHandout> Handouts { get; init; }

    /// <summary>Fail-forward paths that keep the adventure moving if players get stuck.</summary>
    public required IReadOnlyList<FailForwardPath> FailForwardPaths { get; init; }
}

/// <summary>
/// A secret that can be discovered during the adventure.
/// </summary>
public sealed record AdventureSecret
{
    /// <summary>Secret title or label.</summary>
    public required string Title { get; init; }

    /// <summary>The secret content (DM-only until revealed).</summary>
    public required string Content { get; init; }

    /// <summary>Scene where this secret can be discovered.</summary>
    public required string SceneId { get; init; }

    /// <summary>How the secret can be discovered (skill check, investigation, etc.).</summary>
    public required string DiscoveryMethod { get; init; }
}

/// <summary>
/// A handout or document for the players.
/// </summary>
public sealed record AdventureHandout
{
    /// <summary>Handout title.</summary>
    public required string Title { get; init; }

    /// <summary>Handout content text.</summary>
    public required string Content { get; init; }

    /// <summary>Scene where this handout is found.</summary>
    public required string SceneId { get; init; }
}

/// <summary>
/// A fail-forward path that keeps the adventure moving.
/// </summary>
public sealed record FailForwardPath
{
    /// <summary>The situation where players might get stuck.</summary>
    public required string Trigger { get; init; }

    /// <summary>What happens to move the story forward anyway.</summary>
    public required string Resolution { get; init; }

    /// <summary>Scene this applies to.</summary>
    public required string SceneId { get; init; }
}
