namespace Aircane.Domain.Combat;

/// <summary>
/// Applies the Pathfinder 2e <em>elite</em> and <em>weak</em> creature templates. In PF2e these
/// templates apply a static ±2 adjustment to a creature's attack modifiers, AC, saves, skills,
/// perception, and DCs, along with an HP change scaled by the creature's level.
/// </summary>
/// <remarks>
/// The domain <see cref="Combatant"/> tracks HP and conditions but not attack/AC/save numbers, so
/// this adjuster applies the HP change directly (the part we can model authoritatively) and tags
/// the combatant with an <c>Elite</c>/<c>Weak</c> marker condition. The ±2 numeric adjustment is
/// conveyed to the AI/GM via that marker (and PF2e AI guidance) rather than mutating stats the
/// model does not store. Applying a template is idempotent per combatant: an already-elite
/// combatant is not made "double elite".
/// </remarks>
public static class EliteWeakAdjuster
{
    /// <summary>Marker condition name applied to an elite creature.</summary>
    public const string EliteMarker = "Elite";

    /// <summary>Marker condition name applied to a weak creature.</summary>
    public const string WeakMarker = "Weak";

    /// <summary>The flat ±2 adjustment PF2e elite/weak templates apply to most numeric statistics.</summary>
    public const int StatAdjustment = 2;

    /// <summary>
    /// Applies the elite template to the identified combatant: increases HP per the PF2e
    /// level-based table and tags it as <c>Elite</c>. No-op if already elite; removes a prior
    /// <c>Weak</c> marker (templates don't stack).
    /// </summary>
    public static EncounterState ApplyElite(EncounterState state, string combatantId) =>
        ApplyTemplate(state, combatantId, isElite: true);

    /// <summary>
    /// Applies the weak template to the identified combatant: decreases HP per the PF2e
    /// level-based table and tags it as <c>Weak</c>. No-op if already weak; removes a prior
    /// <c>Elite</c> marker (templates don't stack).
    /// </summary>
    public static EncounterState ApplyWeak(EncounterState state, string combatantId) =>
        ApplyTemplate(state, combatantId, isElite: false);

    /// <summary>
    /// The HP adjustment magnitude for the elite/weak templates given the creature's level,
    /// following the PF2e GM Core table.
    /// </summary>
    public static int HpAdjustmentForLevel(int level) => level switch
    {
        <= 1 => 10,
        <= 4 => 15,
        <= 19 => 20,
        _ => 30,
    };

    private static EncounterState ApplyTemplate(EncounterState state, string combatantId, bool isElite)
    {
        var combatant = state.Combatants.FirstOrDefault(c => c.Id == combatantId);
        if (combatant is null)
            return state;

        var marker = isElite ? EliteMarker : WeakMarker;
        var opposite = isElite ? WeakMarker : EliteMarker;

        // Idempotent: don't reapply the same template.
        if (combatant.Conditions.Any(c => string.Equals(c.Name, marker, StringComparison.OrdinalIgnoreCase)))
            return state;

        // Estimate the creature level from Max HP so the HP delta is level-appropriate even though
        // the model does not store a discrete level. This is a heuristic; callers with a known
        // level can pre-set HP accordingly.
        var estimatedLevel = EstimateLevelFromHp(combatant.MaxHp);
        var hpDelta = HpAdjustmentForLevel(estimatedLevel);

        var updated = state.Combatants.Select(c =>
        {
            if (c.Id != combatantId)
                return c;

            var newMax = isElite ? c.MaxHp + hpDelta : Math.Max(1, c.MaxHp - hpDelta);
            var newCurrent = isElite
                ? c.CurrentHp + hpDelta
                : Math.Min(c.CurrentHp, newMax);

            // Drop any opposite marker (templates don't stack) and add this one.
            var conditions = c.Conditions
                .Where(cond => !string.Equals(cond.Name, opposite, StringComparison.OrdinalIgnoreCase))
                .Append(new ConditionInstance { Name = marker, AppliedOnRound = c.Conditions.Count == 0 ? state.Round : state.Round })
                .ToList();

            return c with
            {
                MaxHp = newMax,
                CurrentHp = Math.Max(0, newCurrent),
                Conditions = conditions,
            };
        }).ToList();

        return state with { Combatants = updated };
    }

    private static int EstimateLevelFromHp(int maxHp) => maxHp switch
    {
        <= 20 => 1,
        <= 60 => 3,
        <= 150 => 10,
        _ => 20,
    };
}
