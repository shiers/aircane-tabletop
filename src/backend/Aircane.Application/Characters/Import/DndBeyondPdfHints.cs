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

    // ── DDB printable-sheet OCR caption vocabulary (FEAT-002) ─────────────────────────────────
    //
    // The DDB share-link export has NO AcroForm (FEAT-002-tuning-note.md): the text layer carries
    // only template CAPTIONS as bare words (no "Label: Value" colon pairs), and the values are
    // rasterized pixels. The OCR path anchors value regions to these captions, OCRs them, and maps
    // the caption back to a canonical path. The caption tokens below are the VERIFIED tokens from
    // the tuning note. CaptionMap keys, DndBeyondSheetLayout anchor keys, and the signature tokens
    // in DndBeyondSheetSignature MUST all be the SAME normalized (uppercased/trimmed) tokens.

    // Signature-quorum caption tokens (also used as layout anchor + CaptionMap keys where mapped).
    public const string StrengthCaption = "STRENGTH";
    public const string DexterityCaption = "DEXTERITY";
    public const string ConstitutionCaption = "CONSTITUTION";
    public const string IntelligenceCaption = "INTELLIGENCE";
    public const string WisdomCaption = "WISDOM";
    public const string CharismaCaption = "CHARISMA";
    public const string CharacterNameCaption = "CHARACTER NAME";

    /// <summary>
    /// The combined "class name + level" caption on the printable sheet (e.g. a nearby "Fighter 5"
    /// value). Routed through the existing <c>ClassLevel</c> split in the mapper.
    /// </summary>
    public const string ClassLevelCaption = "CLASS & LEVEL";

    /// <summary>
    /// The page-1 Armor Class caption. On the real DDB printable sheet this is the bare token
    /// <c>ARMOR</c> (NOT <c>ARMOR CLASS</c>); <see cref="CaptionMap"/> accepts both via an alias set.
    /// </summary>
    public const string ArmorCaption = "ARMOR";
    public const string ArmorClassAliasCaption = "ARMOR CLASS";

    /// <summary>The 2024 ancestry caption; its presence selects the 2024 ruleset.</summary>
    public const string SpeciesCaption = "SPECIES";

    /// <summary>The 2014 ancestry caption; its presence (without SPECIES) selects the 2014 ruleset.</summary>
    public const string RaceCaption = "RACE";

    public const string BackgroundCaption = "BACKGROUND";
    public const string SpeedCaption = "SPEED";
    public const string ProficiencyBonusCaption = "PROFICIENCY BONUS";

    /// <summary>
    /// The max-HP caption. Both <c>HP</c> and <c>HIT POINTS</c> tokens appear on the sheet; the
    /// max-HP value is disambiguated by position in <see cref="DndBeyondSheetLayout"/>. This token
    /// is the one that maps to <see cref="CanonicalCharacterPaths.CombatMaxHitPoints"/>.
    /// </summary>
    public const string HitPointsCaption = "HIT POINTS";

    /// <summary>
    /// A signature-quorum caption only; deliberately NOT in <see cref="CaptionMap"/> because
    /// <c>ApplyCanonicalPath</c> has no passive-perception arm (a CaptionMap entry would never map).
    /// </summary>
    public const string PassivePerceptionCaption = "PASSIVE PERCEPTION";

    /// <summary>
    /// DDB printable-sheet caption token → canonical <c>ApplyMapping</c> path, for the OCR path.
    /// Keyed on the VERIFIED caption tokens (uppercased/trimmed). The AC caption <c>ARMOR</c> has an
    /// alias <c>ARMOR CLASS</c> (both → <see cref="CanonicalCharacterPaths.CombatArmorClass"/>).
    /// <para>
    /// <see cref="ClassLevelCaption"/> is intentionally absent: it is handled specially (class/level
    /// split) by the mapper rather than through a direct canonical path. <see cref="PassivePerceptionCaption"/>
    /// is also absent (signature-quorum token only; no canonical apply arm).
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CaptionMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CharacterNameCaption] = CanonicalCharacterPaths.IdentityName,
            [SpeciesCaption] = CanonicalCharacterPaths.IdentityRaceOrAncestry, // 2024
            [RaceCaption] = CanonicalCharacterPaths.IdentityRaceOrAncestry,    // 2014
            [BackgroundCaption] = CanonicalCharacterPaths.IdentityBackground,
            [StrengthCaption] = CanonicalCharacterPaths.AbilityStrength,
            [DexterityCaption] = CanonicalCharacterPaths.AbilityDexterity,
            [ConstitutionCaption] = CanonicalCharacterPaths.AbilityConstitution,
            [IntelligenceCaption] = CanonicalCharacterPaths.AbilityIntelligence,
            [WisdomCaption] = CanonicalCharacterPaths.AbilityWisdom,
            [CharismaCaption] = CanonicalCharacterPaths.AbilityCharisma,
            [ArmorCaption] = CanonicalCharacterPaths.CombatArmorClass,          // verified token
            [ArmorClassAliasCaption] = CanonicalCharacterPaths.CombatArmorClass, // alias (NIT-2)
            [HitPointsCaption] = CanonicalCharacterPaths.CombatMaxHitPoints,
            [SpeedCaption] = CanonicalCharacterPaths.CombatSpeed,
            [ProficiencyBonusCaption] = CanonicalCharacterPaths.CombatProficiencyBonus,
        };
}
