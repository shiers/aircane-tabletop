using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Computes encounter budgets and validates encounters against party composition
/// using the active game system's encounter budget formula.
/// Supports XP budget, creature level comparison, threat rating, and narrative tiers.
/// </summary>
public class EncounterBudgetEngine : IEncounterBudgetEngine
{
    /// <inheritdoc />
    public EncounterBudgetResult ComputeBudget(EncounterBudgetFormula formula, PartyComposition party)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(party);

        var tierBudgets = new Dictionary<string, double>();

        if (formula.DifficultyTiers.Count == 0)
        {
            return new EncounterBudgetResult { TierBudgets = tierBudgets };
        }

        var baseBudget = ComputeBaseBudget(formula, party);

        foreach (var tier in formula.DifficultyTiers)
        {
            tierBudgets[tier.Name] = baseBudget * tier.Multiplier;
        }

        return new EncounterBudgetResult { TierBudgets = tierBudgets };
    }

    /// <inheritdoc />
    public EncounterValidationResult ValidateEncounter(
        EncounterBudgetFormula formula,
        PartyComposition party,
        IReadOnlyList<CreatureThreat> creatures)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(creatures);

        var budgetResult = ComputeBudget(formula, party);
        var totalCreatureCost = creatures.Sum(c => (double)c.Cost);

        if (budgetResult.TierBudgets.Count == 0)
        {
            // No tiers defined — cannot validate, treat as valid
            return new EncounterValidationResult
            {
                IsValid = true,
                TotalCreatureCost = totalCreatureCost,
                MatchedTier = null,
                ExceedsCeiling = false,
                Warning = null
            };
        }

        // Find the matched tier (highest tier whose budget is >= creature cost)
        string? matchedTier = null;
        var tiersOrdered = budgetResult.TierBudgets
            .OrderBy(kvp => kvp.Value)
            .ToList();

        foreach (var (tierName, tierBudget) in tiersOrdered)
        {
            if (totalCreatureCost <= tierBudget)
            {
                matchedTier = tierName;
                break;
            }
        }

        // If no tier matched, the encounter exceeds all tiers
        if (matchedTier is null)
        {
            // Check if it exceeds the ceiling (highest tier)
            matchedTier = tiersOrdered.Last().Key;
        }

        // Determine if it exceeds the ceiling (highest difficulty tier)
        var ceilingBudget = tiersOrdered.Last().Value;
        var exceedsCeiling = totalCreatureCost > ceilingBudget;

        string? warning = null;
        if (exceedsCeiling)
        {
            warning = $"Encounter cost ({totalCreatureCost}) exceeds the maximum difficulty ceiling " +
                      $"'{tiersOrdered.Last().Key}' budget ({ceilingBudget:F1}). Consider reducing encounter difficulty.";
        }

        return new EncounterValidationResult
        {
            IsValid = !exceedsCeiling,
            TotalCreatureCost = totalCreatureCost,
            MatchedTier = matchedTier,
            ExceedsCeiling = exceedsCeiling,
            Warning = warning
        };
    }

    /// <summary>
    /// Computes the base encounter budget from party composition using the formula type.
    /// </summary>
    private static double ComputeBaseBudget(EncounterBudgetFormula formula, PartyComposition party)
    {
        return formula.Type switch
        {
            EncounterBudgetType.XpBudget => ComputeXpBudget(party),
            EncounterBudgetType.CreatureLevel => ComputeCreatureLevelBudget(party),
            EncounterBudgetType.ThreatRating => ComputeThreatRatingBudget(party),
            EncounterBudgetType.NarrativeTiers => ComputeNarrativeBudget(party),
            _ => ComputeXpBudget(party) // Default fallback
        };
    }

    /// <summary>
    /// XP budget: base = average level * party size * 100 (simplified D&D 5e-style).
    /// The multiplier from each tier scales this base.
    /// </summary>
    private static double ComputeXpBudget(PartyComposition party)
    {
        if (party.PartySize == 0 || party.CharacterLevels.Count == 0)
            return 0;

        // Base XP budget scales with average level and party size
        return party.AverageLevel * party.PartySize * 100;
    }

    /// <summary>
    /// Creature level budget: base = average party level * party size (PF2e-style).
    /// Creatures are compared by level difference from party level.
    /// </summary>
    private static double ComputeCreatureLevelBudget(PartyComposition party)
    {
        if (party.PartySize == 0 || party.CharacterLevels.Count == 0)
            return 0;

        // Base budget is party size * average level (simplified PF2e approach)
        return party.AverageLevel * party.PartySize;
    }

    /// <summary>
    /// Threat rating budget: base = party size * average level * 50.
    /// </summary>
    private static double ComputeThreatRatingBudget(PartyComposition party)
    {
        if (party.PartySize == 0 || party.CharacterLevels.Count == 0)
            return 0;

        return party.AverageLevel * party.PartySize * 50;
    }

    /// <summary>
    /// Narrative tiers: base = party size (simple count-based for narrative systems).
    /// </summary>
    private static double ComputeNarrativeBudget(PartyComposition party)
    {
        if (party.PartySize == 0)
            return 0;

        // For narrative systems, budget is simply based on party size
        return party.PartySize;
    }
}
