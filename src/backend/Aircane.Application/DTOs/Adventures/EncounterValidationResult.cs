namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Result of validating a generated encounter against party capabilities
/// using D&amp;D 5e encounter difficulty rules.
/// </summary>
public sealed record EncounterValidationResult
{
    /// <summary>Whether the encounter passes basic validation checks.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Estimated difficulty category: easy, medium, hard, or deadly.</summary>
    public required string EstimatedDifficulty { get; init; }

    /// <summary>Warnings about the encounter (e.g., too hard, too easy, CR parsing issues).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Total estimated XP value of all creatures in the encounter (before multiplier).</summary>
    public required int TotalXP { get; init; }

    /// <summary>Adjusted XP after applying the encounter multiplier for number of monsters.</summary>
    public int AdjustedXP { get; init; }
}
