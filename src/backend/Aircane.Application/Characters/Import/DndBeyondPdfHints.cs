namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps D&amp;D Beyond PDF-export form-field names to canonical <c>ApplyMapping</c> paths. Used by
/// the PDF extractor as a priority override before its heuristic field matching, so a DDB form-
/// fillable sheet maps cleanly even when its field names differ from the generic heuristics.
/// <para>
/// <c>ClassLevel</c> is handled specially (post-split into class name + level) and the 18 skill
/// fields have no canonical path, so they fall through to the extractor's notes/unmapped handling.
/// </para>
/// </summary>
public static class DndBeyondPdfHints
{
    /// <summary>
    /// DDB PDF form-field name → canonical <c>ApplyMapping</c> path. Keys are matched against the
    /// PDF's raw form-field names.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CharacterName"] = CanonicalCharacterPaths.IdentityName,
            ["Race"] = CanonicalCharacterPaths.IdentityRaceOrAncestry,
            ["Background"] = CanonicalCharacterPaths.IdentityBackground,
            ["STR"] = CanonicalCharacterPaths.AbilityStrength,
            ["DEX"] = CanonicalCharacterPaths.AbilityDexterity,
            ["CON"] = CanonicalCharacterPaths.AbilityConstitution,
            ["INT"] = CanonicalCharacterPaths.AbilityIntelligence,
            ["WIS"] = CanonicalCharacterPaths.AbilityWisdom,
            ["CHA"] = CanonicalCharacterPaths.AbilityCharisma,
            ["HPMax"] = CanonicalCharacterPaths.CombatMaxHitPoints,
            ["HPCurrent"] = CanonicalCharacterPaths.CombatHitPoints,
            ["AC"] = CanonicalCharacterPaths.CombatArmorClass,
            ["Speed"] = CanonicalCharacterPaths.CombatSpeed,
            ["ProfBonus"] = CanonicalCharacterPaths.CombatProficiencyBonus,
        };

    /// <summary>
    /// The DDB PDF form-field name that carries a combined "class level" value (e.g. "Fighter 5"),
    /// which the extractor splits into a class name + level after mapping.
    /// </summary>
    public const string ClassLevelFieldName = "ClassLevel";
}
