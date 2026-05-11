using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Defines how a game system calculates encounter difficulty and balance.
/// </summary>
public record EncounterBudgetFormula
{
    /// <summary>The type of encounter budget system (xp_budget, creature_level, threat_rating, narrative_tiers).</summary>
    public EncounterBudgetType Type { get; init; }

    /// <summary>Difficulty tiers with their multipliers or thresholds.</summary>
    public IReadOnlyList<DifficultyTier> DifficultyTiers { get; init; } = [];

    /// <summary>The formula expression for computing the party's encounter budget.</summary>
    public string? Formula { get; init; }

    /// <summary>The field on creatures that represents their cost/threat (e.g., "xp", "level", "threat").</summary>
    public string? CreatureCostField { get; init; }
}

/// <summary>
/// A named difficulty tier with an associated multiplier for encounter budget calculation.
/// </summary>
public record DifficultyTier
{
    /// <summary>Name of this difficulty tier (e.g., "Easy", "Medium", "Hard", "Deadly").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Multiplier applied to the base budget for this tier.</summary>
    public double Multiplier { get; init; }
}
