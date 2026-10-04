namespace Aircane.Application.Characters.Import;

/// <summary>
/// The canonical <c>ApplyMapping</c> path vocabulary, shared by every source mapper so path
/// spellings stay aligned with the save-time <c>CharacterService.ApplyMapping</c> switch.
/// <para>
/// This helper NEVER flattens a <see cref="CanonicalCharacter"/>. It only exposes the finite set of
/// accepted paths, the canonical path-string constants mappers write, and the one-to-one bridge
/// between schema-field-ids (used by form rendering) and canonical paths (used by the save flow).
/// </para>
/// </summary>
public static class CanonicalCharacterPaths
{
    // ── Canonical path-string constants (dotted form, as emitted by mappers) ──────────────────

    public const string IdentityName = "identity.name";
    public const string IdentityRaceOrAncestry = "identity.raceOrAncestry";
    public const string IdentityBackground = "identity.background";
    public const string IdentityAlignment = "identity.alignment";
    public const string IdentityExperiencePoints = "identity.experiencePoints";

    public const string Class = "class";
    public const string Level = "level";

    public const string AbilityStrength = "abilities.strength";
    public const string AbilityDexterity = "abilities.dexterity";
    public const string AbilityConstitution = "abilities.constitution";
    public const string AbilityIntelligence = "abilities.intelligence";
    public const string AbilityWisdom = "abilities.wisdom";
    public const string AbilityCharisma = "abilities.charisma";

    public const string CombatArmorClass = "combat.armorClass";
    public const string CombatHitPoints = "combat.hitPoints";
    public const string CombatMaxHitPoints = "combat.maxHitPoints";
    public const string CombatSpeed = "combat.speed";
    public const string CombatInitiative = "combat.initiative";
    public const string CombatProficiencyBonus = "combat.proficiencyBonus";

    public const string PassivePerception = "passivePerception";

    /// <summary>
    /// Every path <c>CharacterService.ApplyMapping</c> accepts, lowercased for case-insensitive
    /// comparison. This mirrors the <c>switch</c> arms in <c>ApplyMapping</c> exactly (both the
    /// dotted and short aliases). Used by the integration path-acceptance guard; mappers emit the
    /// dotted constants above (whose lowercased forms are all members of this set).
    /// </summary>
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        // Identity
        "identity.name", "name",
        "identity.raceorancestry", "race",
        "identity.background", "background",
        "identity.alignment", "alignment",
        "identity.experiencepoints", "experiencepoints",

        // Class / level
        "class",
        "level",

        // Abilities
        "abilities.strength", "strength",
        "abilities.dexterity", "dexterity",
        "abilities.constitution", "constitution",
        "abilities.intelligence", "intelligence",
        "abilities.wisdom", "wisdom",
        "abilities.charisma", "charisma",

        // Combat
        "combat.armorclass", "armorclass",
        "combat.hitpoints", "combat.currenthitpoints", "hitpoints",
        "combat.maxhitpoints", "maxhitpoints",
        "combat.speed", "speed",
        "combat.initiative", "initiative",
        "combat.proficiencybonus", "proficiencybonus",

        // Derived / computed
        "passiveperception",
    };

    /// <summary>
    /// One-to-one bridge from schema-field-id (used by the FormDescriptor renderer) to canonical
    /// <c>ApplyMapping</c> path. Scoped to the fields that have BOTH a schema id and a canonical
    /// path; fields with no canonical path (PF2e saves, class DC, perception rank) are excluded.
    /// This is NOT a flatten of a character — it is a static vocabulary bridge.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SchemaFieldIdToPath =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = IdentityName,
            ["race"] = IdentityRaceOrAncestry,
            ["ancestry"] = IdentityRaceOrAncestry,
            ["background"] = IdentityBackground,
            ["class"] = Class,
            ["level"] = Level,
            ["str"] = AbilityStrength,
            ["dex"] = AbilityDexterity,
            ["con"] = AbilityConstitution,
            ["int"] = AbilityIntelligence,
            ["wis"] = AbilityWisdom,
            ["cha"] = AbilityCharisma,
            ["ac"] = CombatArmorClass,
            ["hp_max"] = CombatMaxHitPoints,
            ["hp_current"] = CombatHitPoints,
        };
}
