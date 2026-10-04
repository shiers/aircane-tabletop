using System.Globalization;

namespace Aircane.Application.Characters;

/// <summary>
/// The outcome of a <see cref="CharacterDraftSanitizer.Sanitize"/> pass: the human-readable
/// warnings describing each value that was clamped, and the canonical <c>ApplyMapping</c> paths of
/// the adjusted fields so the review UI can flag them for user confirmation.
/// </summary>
/// <param name="Warnings">One entry per clamped field, formatted "field: old -&gt; new".</param>
/// <param name="ReviewPaths">
/// The canonical path (e.g. <c>combat.currentHitPoints</c>, <c>abilities.strength</c>) of each
/// adjusted field. Every path here corresponds to exactly one warning.
/// </param>
public sealed record CharacterDraftSanitizationResult(
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ReviewPaths)
{
    /// <summary>True when no value needed clamping (both collections are empty).</summary>
    public bool IsClean => Warnings.Count == 0 && ReviewPaths.Count == 0;
}

/// <summary>
/// Brings a mapped import draft into the ranges the strict <see cref="CharacterSchemaValidator"/>
/// accepts, so a review-worthy value (a negative current HP, an out-of-range ability sum, an
/// over-20 total level) is clamped-for-storage and flagged for review instead of hard-failing the
/// save. This mirrors the mapper's existing "keep it, flag it" philosophy; the strict validator is
/// deliberately NOT weakened — manual create/update and the post-review save still use it as-is.
/// Never throws. An empty <see cref="CanonicalCharacter.Classes"/> list is left untouched for the
/// validator to reject (the sanitizer never invents a class).
/// </summary>
public static class CharacterDraftSanitizer
{
    private const int MinAbilityScore = 1;
    private const int MaxAbilityScore = 30;
    private const int MinLevel = 1;
    private const int MaxLevel = 20;

    /// <summary>
    /// Clamps the known numeric fields of <paramref name="character"/> in place to the validator's
    /// accepted ranges, returning one warning + one review path for every value actually changed.
    /// Clamping rules:
    /// <list type="bullet">
    ///   <item>each ability score → 1..30;</item>
    ///   <item>each class level → 1..20; if the summed level still exceeds 20, trailing classes are
    ///   reduced (last class first) until the total is 20, keeping at least one class at level ≥ 1;</item>
    ///   <item>Combat.MaxHitPoints → Max(0, max);</item>
    ///   <item>Combat.TemporaryHitPoints → Max(0, temp);</item>
    ///   <item>Combat.CurrentHitPoints → Max(0, Min(current, MaxHitPoints));</item>
    ///   <item>any HitDie ≤ 0 → a class-name-derived default (d6/d8/d10/d12), else d8.</item>
    /// </list>
    /// </summary>
    public static CharacterDraftSanitizationResult Sanitize(CanonicalCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var warnings = new List<string>();
        var reviewPaths = new List<string>();

        SanitizeAbilities(character, warnings, reviewPaths);
        SanitizeClasses(character, warnings, reviewPaths);
        SanitizeHitPoints(character, warnings, reviewPaths);

        return new CharacterDraftSanitizationResult(warnings, reviewPaths);
    }

    private static void SanitizeAbilities(
        CanonicalCharacter character,
        List<string> warnings,
        List<string> reviewPaths)
    {
        var abilities = character.Abilities;

        abilities.Strength = ClampAbility(
            "Strength", Import.CanonicalCharacterPaths.AbilityStrength, abilities.Strength, warnings, reviewPaths);
        abilities.Dexterity = ClampAbility(
            "Dexterity", Import.CanonicalCharacterPaths.AbilityDexterity, abilities.Dexterity, warnings, reviewPaths);
        abilities.Constitution = ClampAbility(
            "Constitution", Import.CanonicalCharacterPaths.AbilityConstitution, abilities.Constitution, warnings, reviewPaths);
        abilities.Intelligence = ClampAbility(
            "Intelligence", Import.CanonicalCharacterPaths.AbilityIntelligence, abilities.Intelligence, warnings, reviewPaths);
        abilities.Wisdom = ClampAbility(
            "Wisdom", Import.CanonicalCharacterPaths.AbilityWisdom, abilities.Wisdom, warnings, reviewPaths);
        abilities.Charisma = ClampAbility(
            "Charisma", Import.CanonicalCharacterPaths.AbilityCharisma, abilities.Charisma, warnings, reviewPaths);
    }

    private static int ClampAbility(
        string label,
        string path,
        int value,
        List<string> warnings,
        List<string> reviewPaths)
    {
        var clamped = Math.Clamp(value, MinAbilityScore, MaxAbilityScore);
        if (clamped != value)
            Record(warnings, reviewPaths, $"{label} ability score", value, clamped, path);
        return clamped;
    }

    private static void SanitizeClasses(
        CanonicalCharacter character,
        List<string> warnings,
        List<string> reviewPaths)
    {
        var classes = character.Classes;
        if (classes.Count == 0)
            return; // Leave the empty list for the validator to reject; never invent a class.

        // Per-class level into 1..20, and any non-positive hit die to a class default.
        for (var i = 0; i < classes.Count; i++)
        {
            var cls = classes[i];

            var clampedLevel = Math.Clamp(cls.Level, MinLevel, MaxLevel);
            if (clampedLevel != cls.Level)
            {
                Record(
                    warnings, reviewPaths,
                    $"Class {i + 1} ({DescribeClass(cls)}) level",
                    cls.Level, clampedLevel, Import.CanonicalCharacterPaths.Level);
                cls.Level = clampedLevel;
            }

            if (cls.HitDie <= 0)
            {
                var defaultDie = DefaultHitDie(cls.ClassName);
                Record(
                    warnings, reviewPaths,
                    $"Class {i + 1} ({DescribeClass(cls)}) hit die",
                    cls.HitDie, defaultDie, Import.CanonicalCharacterPaths.Class);
                cls.HitDie = defaultDie;
            }
        }

        // Total level must be 1..20. If the sum still exceeds 20 after the per-class clamp, reduce
        // trailing classes first (last entry down to level 1) until the total is 20, so the primary
        // (first) class keeps as much of its level as possible and at least one class stays ≥ 1.
        var total = classes.Sum(c => c.Level);
        if (total <= MaxLevel)
            return;

        var before = total;
        for (var i = classes.Count - 1; i >= 0 && total > MaxLevel; i--)
        {
            var reducible = classes[i].Level - MinLevel; // keep each class at ≥ 1
            if (reducible <= 0)
                continue;

            var reduceBy = Math.Min(reducible, total - MaxLevel);
            classes[i].Level -= reduceBy;
            total -= reduceBy;
        }

        Record(
            warnings, reviewPaths,
            "Total character level",
            before, total, Import.CanonicalCharacterPaths.Level);
    }

    private static void SanitizeHitPoints(
        CanonicalCharacter character,
        List<string> warnings,
        List<string> reviewPaths)
    {
        var combat = character.Combat;

        var max = Math.Max(0, combat.MaxHitPoints);
        if (max != combat.MaxHitPoints)
            Record(
                warnings, reviewPaths, "Max hit points",
                combat.MaxHitPoints, max, Import.CanonicalCharacterPaths.CombatMaxHitPoints);
        combat.MaxHitPoints = max;

        var temp = Math.Max(0, combat.TemporaryHitPoints);
        if (temp != combat.TemporaryHitPoints)
            Record(
                warnings, reviewPaths, "Temporary hit points",
                combat.TemporaryHitPoints, temp, "combat.temporaryHitPoints");
        combat.TemporaryHitPoints = temp;

        var current = Math.Max(0, Math.Min(combat.CurrentHitPoints, combat.MaxHitPoints));
        if (current != combat.CurrentHitPoints)
            Record(
                warnings, reviewPaths, "Current hit points",
                combat.CurrentHitPoints, current, Import.CanonicalCharacterPaths.CombatHitPoints);
        combat.CurrentHitPoints = current;
    }

    /// <summary>Standard 5e hit die by class name; defaults to d8 for unknown classes.</summary>
    private static int DefaultHitDie(string? className) =>
        (className ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "barbarian" => 12,
            "fighter" or "paladin" or "ranger" => 10,
            "sorcerer" or "wizard" => 6,
            _ => 8,
        };

    private static string DescribeClass(CharacterClass cls) =>
        string.IsNullOrWhiteSpace(cls.ClassName) ? "Unnamed" : cls.ClassName;

    private static void Record(
        List<string> warnings,
        List<string> reviewPaths,
        string label,
        int oldValue,
        int newValue,
        string path)
    {
        warnings.Add(string.Format(
            CultureInfo.InvariantCulture,
            "{0} adjusted from {1} to {2} to fit the allowed range. Please review.",
            label, oldValue, newValue));
        reviewPaths.Add(path);
    }
}
