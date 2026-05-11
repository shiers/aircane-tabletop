namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// The complete result of resolving a dice roll, including raw results, kept results,
/// modifiers, totals, success counts, outcome tiers, and exploded results.
/// </summary>
public record RollResolution
{
    /// <summary>All dice results before keep/drop filtering.</summary>
    public IReadOnlyList<int> RawResults { get; init; } = [];

    /// <summary>Dice results after keep/drop filtering. Same as RawResults if no keep/drop applied.</summary>
    public IReadOnlyList<int> KeptResults { get; init; } = [];

    /// <summary>The modifier applied to the roll total.</summary>
    public int Modifier { get; init; }

    /// <summary>The final total (sum of kept results + modifier). Used for standard/fudge/percentile rolls.</summary>
    public int Total { get; init; }

    /// <summary>Number of successes for pool-based systems. Null for non-pool rolls.</summary>
    public int? SuccessCount { get; init; }

    /// <summary>The outcome tier name for threshold band systems (e.g., "Miss", "Weak Hit", "Strong Hit"). Null when not applicable.</summary>
    public string? OutcomeTier { get; init; }

    /// <summary>Additional results from exploding dice re-rolls. Null when no explosions occurred.</summary>
    public IReadOnlyList<int>? ExplodedResults { get; init; }
}
