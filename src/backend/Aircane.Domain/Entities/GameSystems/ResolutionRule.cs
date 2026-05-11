using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Describes how a game system resolves an action (e.g., ability check, attack roll, saving throw).
/// </summary>
public record ResolutionRule
{
    /// <summary>A unique name for this resolution rule (e.g., "abilityCheck", "attackRoll").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The type of resolution (target_number, opposed, degrees_of_success, margin, threshold_bands).</summary>
    public ResolutionRuleType Type { get; init; }

    /// <summary>Which dice convention to use for this resolution (references a DiceConvention name).</summary>
    public string Roll { get; init; } = string.Empty;

    /// <summary>Comparison operator for target number rules (e.g., ">=", ">", "<=").</summary>
    public string? Comparison { get; init; }

    /// <summary>Source of the target number (e.g., "dc", "ac").</summary>
    public string? TargetSource { get; init; }

    /// <summary>Critical success condition (e.g., natural 20).</summary>
    public CriticalCondition? CriticalSuccess { get; init; }

    /// <summary>Critical failure condition (e.g., natural 1).</summary>
    public CriticalCondition? CriticalFailure { get; init; }

    /// <summary>Degrees of success thresholds for systems that use them (PF2e, PbtA).</summary>
    public IReadOnlyList<DegreeThreshold>? DegreesOfSuccess { get; init; }

    /// <summary>Tie-breaking specification for opposed rolls.</summary>
    public string? TieBreaker { get; init; }
}

/// <summary>
/// Defines a critical success or failure condition based on a natural die roll.
/// </summary>
public record CriticalCondition
{
    /// <summary>The natural roll value that triggers this condition.</summary>
    public int NaturalRoll { get; init; }
}

/// <summary>
/// Defines a threshold band for degrees of success resolution.
/// </summary>
public record DegreeThreshold
{
    /// <summary>Name of this degree (e.g., "Critical Success", "Success", "Failure", "Critical Failure").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Minimum value (inclusive) for this threshold band. Null means no lower bound.</summary>
    public int? MinValue { get; init; }

    /// <summary>Maximum value (inclusive) for this threshold band. Null means no upper bound.</summary>
    public int? MaxValue { get; init; }
}
