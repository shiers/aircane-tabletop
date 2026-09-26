namespace Aircane.Domain.Combat;

/// <summary>
/// Pure, deterministic combat state transitions. Every method takes an
/// <see cref="EncounterState"/> and returns a new one, never mutating the input. All game rules
/// here are D&amp;D-5e-style defaults; system-specific numbers (death-save thresholds, etc.) are
/// parameters or constants documented inline so a future adapter can override them.
/// </summary>
public static class CombatEngine
{
    /// <summary>Death-save successes required to stabilize.</summary>
    public const int DeathSaveSuccessesToStabilize = 3;

    /// <summary>Death-save failures required to die.</summary>
    public const int DeathSaveFailuresToDie = 3;

    /// <summary>
    /// Begins a new encounter with the given combatants. Initiative is not yet rolled; the
    /// encounter is inactive until <see cref="RollInitiative"/> establishes turn order.
    /// </summary>
    public static EncounterState StartEncounter(IEnumerable<Combatant> combatants) =>
        new()
        {
            EncounterId = Guid.NewGuid().ToString(),
            IsActive = false,
            Round = 0,
            TurnIndex = -1,
            InitiativeOrder = [],
            Combatants = combatants.ToList(),
        };

    /// <summary>
    /// Sets initiative values for combatants (by id) and activates the encounter, ordering turns
    /// highest-first. Combatants not present in <paramref name="initiativeById"/> keep their
    /// existing initiative. Ties break by descending initiative then by insertion order.
    /// </summary>
    public static EncounterState RollInitiative(
        EncounterState state,
        IReadOnlyDictionary<string, int> initiativeById)
    {
        var updated = state.Combatants
            .Select(c => initiativeById.TryGetValue(c.Id, out var init) ? c with { Initiative = init } : c)
            .ToList();

        var order = updated
            .OrderByDescending(c => c.Initiative)
            .Select(c => c.Id)
            .ToList();

        return state with
        {
            Combatants = updated,
            InitiativeOrder = order,
            IsActive = true,
            Round = 1,
            TurnIndex = 0,
        };
    }

    /// <summary>
    /// Advances to the next combatant in initiative order. Wrapping past the end increments the
    /// round and ticks all combatants' condition durations by one round. No-op if inactive.
    /// </summary>
    public static EncounterState AdvanceTurn(EncounterState state)
    {
        if (!state.IsActive || state.InitiativeOrder.Count == 0)
            return state;

        var nextIndex = state.TurnIndex + 1;
        if (nextIndex < state.InitiativeOrder.Count)
            return state with { TurnIndex = nextIndex };

        // Wrapped to a new round: reset to the top and tick durations.
        return TickConditions(state with { TurnIndex = 0, Round = state.Round + 1 });
    }

    /// <summary>
    /// Applies damage to a combatant. Temporary HP absorbs first, then current HP (floored at 0).
    /// A player character reduced to 0 HP begins death saves; an NPC reduced to 0 HP is dead.
    /// </summary>
    public static EncounterState ApplyDamage(EncounterState state, string combatantId, int amount)
    {
        if (amount <= 0)
            return state;

        return MapCombatant(state, combatantId, c =>
        {
            var remaining = amount;
            var temp = c.TemporaryHp;
            if (temp > 0)
            {
                var absorbed = Math.Min(temp, remaining);
                temp -= absorbed;
                remaining -= absorbed;
            }

            var newHp = Math.Max(0, c.CurrentHp - remaining);
            var updated = c with { CurrentHp = newHp, TemporaryHp = temp };

            if (newHp == 0 && c.IsPlayerCharacter)
            {
                // Downed PC starts (or continues) death saves; instant death rules are left to
                // an explicit DeathSave/adjudication rather than auto-killing here.
                updated = updated with
                {
                    DeathSaves = c.DeathSaves ?? new DeathSaveState(),
                };
            }

            return updated;
        });
    }

    /// <summary>
    /// Heals a combatant up to its maximum HP. Healing a downed player character above 0 clears
    /// its death-save state (it is no longer dying).
    /// </summary>
    public static EncounterState ApplyHealing(EncounterState state, string combatantId, int amount)
    {
        if (amount <= 0)
            return state;

        return MapCombatant(state, combatantId, c =>
        {
            var newHp = Math.Min(c.MaxHp, c.CurrentHp + amount);
            var updated = c with { CurrentHp = newHp };
            if (newHp > 0 && c.DeathSaves is not null)
                updated = updated with { DeathSaves = null };
            return updated;
        });
    }

    /// <summary>
    /// Adds or refreshes a condition on a combatant. If the condition is already present, its
    /// remaining duration is updated to the new value.
    /// </summary>
    public static EncounterState ApplyCondition(
        EncounterState state,
        string combatantId,
        string conditionName,
        int? remainingRounds = null,
        string? endCondition = null)
    {
        return MapCombatant(state, combatantId, c =>
        {
            var existing = c.Conditions.FirstOrDefault(
                x => string.Equals(x.Name, conditionName, StringComparison.OrdinalIgnoreCase));

            var instance = new ConditionInstance
            {
                Name = conditionName,
                RemainingRounds = remainingRounds,
                AppliedOnRound = state.Round,
                EndCondition = endCondition,
            };

            var conditions = existing is null
                ? c.Conditions.Append(instance).ToList()
                : c.Conditions
                    .Select(x => x == existing ? instance : x)
                    .ToList();

            return c with { Conditions = conditions };
        });
    }

    /// <summary>Removes a condition (by name, case-insensitive) from a combatant.</summary>
    public static EncounterState RemoveCondition(EncounterState state, string combatantId, string conditionName)
    {
        return MapCombatant(state, combatantId, c => c with
        {
            Conditions = c.Conditions
                .Where(x => !string.Equals(x.Name, conditionName, StringComparison.OrdinalIgnoreCase))
                .ToList(),
        });
    }

    /// <summary>
    /// Decrements the remaining duration of every timed condition by one round and drops any that
    /// reach zero. Conditions with null (indefinite) duration are left untouched.
    /// </summary>
    public static EncounterState TickConditions(EncounterState state)
    {
        var combatants = state.Combatants.Select(c => c with
        {
            Conditions = c.Conditions
                .Select(x => x.RemainingRounds is null ? x : x with { RemainingRounds = x.RemainingRounds - 1 })
                .Where(x => x.RemainingRounds is null || x.RemainingRounds > 0)
                .ToList(),
        }).ToList();

        return state with { Combatants = combatants };
    }

    /// <summary>
    /// Records a death-saving throw for a downed player character. A success at 3 successes
    /// stabilizes; a failure at 3 failures kills. A natural-20-style recovery is modeled by the
    /// caller passing <paramref name="recoversHp"/> = true (regain 1 HP, clear saves).
    /// </summary>
    public static EncounterState RecordDeathSave(
        EncounterState state,
        string combatantId,
        bool success,
        bool recoversHp = false)
    {
        return MapCombatant(state, combatantId, c =>
        {
            if (!c.IsPlayerCharacter || c.CurrentHp > 0)
                return c; // Only downed PCs make death saves.

            if (recoversHp)
                return c with { CurrentHp = 1, DeathSaves = null };

            var saves = c.DeathSaves ?? new DeathSaveState();
            saves = success
                ? saves with { Successes = saves.Successes + 1 }
                : saves with { Failures = saves.Failures + 1 };

            if (saves.Successes >= DeathSaveSuccessesToStabilize)
                saves = saves with { IsStable = true };

            return c with { DeathSaves = saves };
        });
    }

    private static EncounterState MapCombatant(
        EncounterState state,
        string combatantId,
        Func<Combatant, Combatant> map)
    {
        var combatants = state.Combatants
            .Select(c => c.Id == combatantId ? map(c) : c)
            .ToList();
        return state with { Combatants = combatants };
    }
}
