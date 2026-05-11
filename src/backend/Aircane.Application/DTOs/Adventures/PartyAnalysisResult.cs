namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Result of analyzing a party's composition and capabilities from their character data.
/// Used by the adventure generation pipeline to tailor encounters, challenges, and alternate paths.
/// </summary>
public sealed record PartyAnalysisResult
{
    /// <summary>Number of characters in the party.</summary>
    public required int PartySize { get; init; }

    /// <summary>Average character level across the party.</summary>
    public required double AverageLevel { get; init; }

    /// <summary>Minimum level in the party.</summary>
    public required int MinLevel { get; init; }

    /// <summary>Maximum level in the party.</summary>
    public required int MaxLevel { get; init; }

    /// <summary>Class names with their counts in the party (e.g., "Fighter" → 2).</summary>
    public required IReadOnlyDictionary<string, int> Classes { get; init; }

    /// <summary>Average Armor Class across the party.</summary>
    public required double AverageAC { get; init; }

    /// <summary>Average max hit points across the party.</summary>
    public required double AverageHP { get; init; }

    /// <summary>Total max hit points of the entire party.</summary>
    public required int TotalHP { get; init; }

    /// <summary>Whether any character has healing spells or abilities.</summary>
    public required bool HasHealing { get; init; }

    /// <summary>Whether any character has ranged attacks (weapon or spell).</summary>
    public required bool HasRangedAttacks { get; init; }

    /// <summary>Whether any character has spellcasting capability.</summary>
    public required bool HasMagic { get; init; }

    /// <summary>Whether any character is proficient in Stealth.</summary>
    public required bool HasStealth { get; init; }

    /// <summary>Whether any character is proficient in Perception.</summary>
    public required bool HasPerception { get; init; }

    /// <summary>Summary of party strengths and notable capabilities.</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>Summary of party weaknesses and gaps.</summary>
    public required IReadOnlyList<string> Weaknesses { get; init; }
}
