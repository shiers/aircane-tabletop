namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Provides AI-specific guidance for running a game system, including tone, mechanical notes,
/// and common mistakes to avoid.
/// </summary>
public record AiGuidance
{
    /// <summary>Notes to include in the AI system prompt about this game system's core mechanics.</summary>
    public string? SystemPromptNotes { get; init; }

    /// <summary>Guidance on tone, pacing, and narrative style for this system.</summary>
    public string? ToneGuidance { get; init; }

    /// <summary>Specific mechanical notes the AI should follow (e.g., "Always ask for ability checks using d20+modifier vs DC").</summary>
    public string? MechanicalNotes { get; init; }

    /// <summary>Common mistakes the AI should avoid for this system.</summary>
    public IReadOnlyList<string> CommonMistakes { get; init; } = [];

    /// <summary>Example of how roll requests should be formatted for this system.</summary>
    public string? RollFormatExample { get; init; }
}
