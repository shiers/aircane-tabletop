using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Computes encounter budgets and validates encounters against party composition
/// using the active game system's encounter budget formula.
/// </summary>
public interface IEncounterBudgetEngine
{
    /// <summary>
    /// Computes the encounter budget for a party using the system's formula.
    /// </summary>
    /// <param name="formula">The encounter budget formula from the game system definition.</param>
    /// <param name="party">The party composition (size, levels).</param>
    /// <returns>Budget results with tier-specific budgets.</returns>
    EncounterBudgetResult ComputeBudget(EncounterBudgetFormula formula, PartyComposition party);

    /// <summary>
    /// Validates a generated encounter against the computed budget.
    /// </summary>
    /// <param name="formula">The encounter budget formula from the game system definition.</param>
    /// <param name="party">The party composition (size, levels).</param>
    /// <param name="creatures">The creatures in the encounter with their costs.</param>
    /// <returns>Validation result indicating whether the encounter is within budget.</returns>
    EncounterValidationResult ValidateEncounter(
        EncounterBudgetFormula formula,
        PartyComposition party,
        IReadOnlyList<CreatureThreat> creatures);
}

/// <summary>
/// Represents the composition of a player party for encounter budget calculations.
/// </summary>
public record PartyComposition
{
    /// <summary>Number of characters in the party.</summary>
    public int PartySize { get; init; }

    /// <summary>Individual character levels.</summary>
    public IReadOnlyList<int> CharacterLevels { get; init; } = [];

    /// <summary>Average level of the party.</summary>
    public double AverageLevel => CharacterLevels.Count > 0 ? CharacterLevels.Average() : 0;
}

/// <summary>
/// Represents a creature's threat/cost in an encounter.
/// </summary>
public record CreatureThreat
{
    /// <summary>Name of the creature.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Cost of the creature (XP, level, threat rating, etc.).</summary>
    public int Cost { get; init; }
}

/// <summary>
/// Result of computing encounter budgets for all difficulty tiers.
/// </summary>
public record EncounterBudgetResult
{
    /// <summary>Budget values keyed by difficulty tier name.</summary>
    public IReadOnlyDictionary<string, double> TierBudgets { get; init; } = new Dictionary<string, double>();
}

/// <summary>
/// Result of validating an encounter against the computed budget.
/// </summary>
public record EncounterValidationResult
{
    /// <summary>Whether the encounter is within the budget ceiling.</summary>
    public bool IsValid { get; init; }

    /// <summary>Total cost of all creatures in the encounter.</summary>
    public double TotalCreatureCost { get; init; }

    /// <summary>The difficulty tier that best matches the encounter cost, if any.</summary>
    public string? MatchedTier { get; init; }

    /// <summary>Whether the encounter exceeds the highest difficulty tier's budget.</summary>
    public bool ExceedsCeiling { get; init; }

    /// <summary>Warning message when the encounter is borderline or exceeds ceiling.</summary>
    public string? Warning { get; init; }
}
