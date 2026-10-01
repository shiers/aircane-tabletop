namespace Aircane.Domain.Enums;

/// <summary>
/// Defines the type of encounter budget formula used by a game system.
/// </summary>
public enum EncounterBudgetType
{
    /// <summary>XP budget system (D&amp;D 5e).</summary>
    XpBudget,

    /// <summary>Creature level comparison (Pathfinder 2e).</summary>
    CreatureLevel,

    /// <summary>Threat rating formulas.</summary>
    ThreatRating,

    /// <summary>Narrative difficulty tiers for systems without mechanical balance (PbtA, FATE).</summary>
    NarrativeTiers
}
