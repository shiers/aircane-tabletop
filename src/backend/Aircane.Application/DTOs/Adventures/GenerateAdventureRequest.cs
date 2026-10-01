namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Request body for starting an adventure generation pipeline.
/// </summary>
public sealed record GenerateAdventureRequest
{
    /// <summary>
    /// Adventure mode: Solo or Group.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Ruleset to use for generation (e.g., "D&amp;D 5e 2014", "Pathfinder 2e").
    /// </summary>
    public required string Ruleset { get; init; }

    /// <summary>
    /// Game system identifier.
    /// </summary>
    public required string GameSystem { get; init; }

    /// <summary>
    /// Number of players in the party (primarily for Group mode).
    /// </summary>
    public int PartySize { get; init; } = 1;

    /// <summary>
    /// Average character level of the party.
    /// </summary>
    public int AverageLevel { get; init; } = 1;

    /// <summary>
    /// Narrative tone (e.g., "dark", "lighthearted", "epic", "horror", "comedic").
    /// </summary>
    public required string Tone { get; init; }

    /// <summary>
    /// Adventure length (e.g., "one-shot", "short", "medium", "long").
    /// </summary>
    public required string Length { get; init; }

    /// <summary>
    /// Encounter difficulty (e.g., "easy", "medium", "hard", "deadly").
    /// </summary>
    public required string Difficulty { get; init; }

    /// <summary>
    /// Percentage of content dedicated to combat encounters (0-100).
    /// </summary>
    public int CombatRatio { get; init; }

    /// <summary>
    /// Percentage of content dedicated to exploration (0-100).
    /// </summary>
    public int ExplorationRatio { get; init; }

    /// <summary>
    /// Percentage of content dedicated to roleplay (0-100).
    /// </summary>
    public int RoleplayRatio { get; init; }

    /// <summary>
    /// Optional setting or theme for the adventure.
    /// </summary>
    public string? Setting { get; init; }

    /// <summary>
    /// Optional character IDs for party analysis during generation.
    /// </summary>
    public List<Guid>? CharacterIds { get; init; }
}
