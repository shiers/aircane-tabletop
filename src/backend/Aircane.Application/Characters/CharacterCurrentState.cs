namespace Aircane.Application.Characters;

/// <summary>
/// Runtime mutable state for a character, stored in <c>Character.CurrentStateJson</c>.
/// Tracks values that change frequently during play without modifying the canonical sheet.
/// </summary>
public sealed class CharacterCurrentState
{
    /// <summary>Current hit points (may differ from the canonical sheet after damage/healing).</summary>
    public int CurrentHitPoints { get; set; }

    /// <summary>Temporary hit points granted by spells or abilities.</summary>
    public int TemporaryHitPoints { get; set; }

    /// <summary>Death saving throw successes (0–3).</summary>
    public int DeathSaveSuccesses { get; set; }

    /// <summary>Death saving throw failures (0–3).</summary>
    public int DeathSaveFailures { get; set; }

    /// <summary>
    /// Number of spell slots used per spell level.
    /// Key = spell level (1–9), Value = number of slots used.
    /// </summary>
    public Dictionary<int, int> UsedSpellSlots { get; set; } = [];

    /// <summary>
    /// Number of uses consumed for each named resource.
    /// Key = resource name, Value = number of uses consumed.
    /// </summary>
    public Dictionary<string, int> UsedResources { get; set; } = [];

    /// <summary>Active conditions, e.g. "Poisoned", "Prone", "Blinded".</summary>
    public List<string> Conditions { get; set; } = [];

    /// <summary>Exhaustion level (0–6 in D&amp;D 5e).</summary>
    public int ExhaustionLevel { get; set; }

    /// <summary>Whether the character is currently concentrating on a spell.</summary>
    public bool IsConcentrating { get; set; }

    /// <summary>Name of the spell being concentrated on, if any.</summary>
    public string? ConcentrationSpell { get; set; }

    /// <summary>Whether the character has used their reaction this round.</summary>
    public bool ReactionUsed { get; set; }

    /// <summary>Whether the character has used their bonus action this turn.</summary>
    public bool BonusActionUsed { get; set; }

    /// <summary>Inspiration die held (true = has inspiration).</summary>
    public bool HasInspiration { get; set; }
}
