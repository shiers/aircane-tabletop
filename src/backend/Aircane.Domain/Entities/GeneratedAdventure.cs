namespace Aircane.Domain.Entities;

/// <summary>
/// Persisted generated adventure containing all stage outputs as JSON.
/// Stored as a single entity with JSON columns for each stage's output.
/// </summary>
public class GeneratedAdventure
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Adventure title (from pitch stage).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Current status: Draft, Approved, Active, Rejected.
    /// </summary>
    public GeneratedAdventureStatus Status { get; set; } = GeneratedAdventureStatus.Draft;

    /// <summary>The original generation request parameters serialized as JSON.</summary>
    public string RequestJson { get; set; } = "{}";

    /// <summary>Party analysis result JSON (null if no characters were provided).</summary>
    public string? PartyAnalysisJson { get; set; }

    /// <summary>Pitch stage output JSON (title, hook, summary).</summary>
    public string? PitchJson { get; set; }

    /// <summary>Outline stage output JSON (scene summaries).</summary>
    public string? OutlineJson { get; set; }

    /// <summary>Scenes stage output JSON (detailed scene descriptions).</summary>
    public string? ScenesJson { get; set; }

    /// <summary>NPCs stage output JSON.</summary>
    public string? NpcsJson { get; set; }

    /// <summary>Encounters stage output JSON.</summary>
    public string? EncountersJson { get; set; }

    /// <summary>Treasure stage output JSON.</summary>
    public string? TreasureJson { get; set; }

    /// <summary>Clues/secrets stage output JSON.</summary>
    public string? CluesJson { get; set; }

    /// <summary>Ruleset used for generation.</summary>
    public string Ruleset { get; set; } = string.Empty;

    /// <summary>Game system used for generation.</summary>
    public string GameSystem { get; set; } = string.Empty;

    /// <summary>When the adventure was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the adventure was last updated.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Status of a generated adventure in its lifecycle.
/// </summary>
public enum GeneratedAdventureStatus
{
    /// <summary>Adventure is a draft, still being generated or reviewed.</summary>
    Draft = 0,

    /// <summary>Adventure has been approved by the host and is playable.</summary>
    Approved = 1,

    /// <summary>Adventure is currently being played in a campaign.</summary>
    Active = 2,

    /// <summary>Adventure was rejected by the host.</summary>
    Rejected = 3,
}
