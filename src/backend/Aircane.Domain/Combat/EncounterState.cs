namespace Aircane.Domain.Combat;

/// <summary>
/// The live state of a combat encounter, stored (serialized to JSON) inside the campaign
/// state snapshot under the <c>encounter</c> key. This is the authoritative model the combat
/// engine reads and mutates; damage, healing, conditions, initiative, turn order, and death
/// saves all operate on it.
/// </summary>
public sealed record EncounterState
{
    /// <summary>Stable identifier for this encounter instance.</summary>
    public string EncounterId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>True while the encounter is active (initiative rolled, turns advancing).</summary>
    public bool IsActive { get; init; }

    /// <summary>The current round number (1-based). 0 before initiative is rolled.</summary>
    public int Round { get; init; }

    /// <summary>
    /// Index into <see cref="InitiativeOrder"/> of the combatant whose turn it currently is.
    /// -1 before initiative is rolled.
    /// </summary>
    public int TurnIndex { get; init; } = -1;

    /// <summary>
    /// Combatant IDs in initiative order (highest initiative first). Empty until initiative is rolled.
    /// </summary>
    public IReadOnlyList<string> InitiativeOrder { get; init; } = [];

    /// <summary>All combatants participating in the encounter, keyed by combatant ID.</summary>
    public IReadOnlyList<Combatant> Combatants { get; init; } = [];

    /// <summary>The combatant whose turn it currently is, or null if none/invalid.</summary>
    public Combatant? ActiveCombatant =>
        IsActive && TurnIndex >= 0 && TurnIndex < InitiativeOrder.Count
            ? Combatants.FirstOrDefault(c => c.Id == InitiativeOrder[TurnIndex])
            : null;
}

/// <summary>
/// A single participant in a combat encounter (a player character or an NPC/monster).
/// </summary>
public sealed record Combatant
{
    /// <summary>Stable combatant ID. For player characters this is the Character ID as a string.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True for player characters, false for GM-controlled NPCs/monsters.</summary>
    public bool IsPlayerCharacter { get; init; }

    /// <summary>Current hit points. May drop to 0 (unconscious/dying for PCs, dead for NPCs).</summary>
    public int CurrentHp { get; init; }

    /// <summary>Maximum hit points.</summary>
    public int MaxHp { get; init; }

    /// <summary>Temporary hit points, absorbed before <see cref="CurrentHp"/>.</summary>
    public int TemporaryHp { get; init; }

    /// <summary>Rolled initiative value; higher acts first.</summary>
    public int Initiative { get; init; }

    /// <summary>Active conditions on this combatant, each with its own remaining duration.</summary>
    public IReadOnlyList<ConditionInstance> Conditions { get; init; } = [];

    /// <summary>Death-save tracking for downed player characters. Null when not dying.</summary>
    public DeathSaveState? DeathSaves { get; init; }

    /// <summary>True when the combatant is at 0 HP.</summary>
    public bool IsDowned => CurrentHp <= 0;

    /// <summary>True when a downed player character has died via failed death saves.</summary>
    public bool IsDead => DeathSaves?.IsDead ?? (!IsPlayerCharacter && CurrentHp <= 0);
}

/// <summary>
/// An applied condition instance with its own remaining duration, distinct from the game
/// system's <c>ConditionDefinition</c> (which describes the condition's rules).
/// </summary>
public sealed record ConditionInstance
{
    /// <summary>Condition name (e.g. "Poisoned", "Prone").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Rounds remaining before the condition expires. Null means indefinite (until removed
    /// manually or by a non-duration trigger such as a saving throw or rest).
    /// </summary>
    public int? RemainingRounds { get; init; }

    /// <summary>The round the condition was applied, for auditing.</summary>
    public int AppliedOnRound { get; init; }

    /// <summary>Optional note on how the condition ends (mirrors ConditionDefinition.EndCondition).</summary>
    public string? EndCondition { get; init; }
}

/// <summary>
/// Death-saving-throw state for a downed player character (D&amp;D 5e style: three successes
/// stabilize, three failures kill).
/// </summary>
public sealed record DeathSaveState
{
    public int Successes { get; init; }
    public int Failures { get; init; }

    /// <summary>True once the character has stabilized (3 successes) or recovered HP.</summary>
    public bool IsStable { get; init; }

    /// <summary>True once the character has accumulated 3 death-save failures.</summary>
    public bool IsDead => Failures >= 3;
}
