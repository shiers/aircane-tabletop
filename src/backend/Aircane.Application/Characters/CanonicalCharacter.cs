namespace Aircane.Application.Characters;

/// <summary>
/// Root model for the canonical character JSON schema.
/// Stored in <c>Character.CanonicalJson</c>.
/// Designed to be system-agnostic enough to support D&amp;D 5e 2014 and future Pathfinder 2e.
/// All optional fields use nullable types so the schema can be partially populated.
/// </summary>
public sealed class CanonicalCharacter
{
    /// <summary>Character identity and biographical information.</summary>
    public CharacterIdentity Identity { get; set; } = new();

    /// <summary>One or more class entries (multiclassing is supported).</summary>
    public List<CharacterClass> Classes { get; set; } = [];

    /// <summary>The six core ability scores.</summary>
    public AbilityScores Abilities { get; set; } = new();

    /// <summary>Saving throw proficiency flags for each ability.</summary>
    public SavingThrows SavingThrows { get; set; } = new();

    /// <summary>Skill proficiency entries.</summary>
    public List<SkillProficiency> Skills { get; set; } = [];

    /// <summary>Core combat statistics.</summary>
    public CombatStats Combat { get; set; } = new();

    /// <summary>Death saving throw tracker.</summary>
    public DeathSaves DeathSaves { get; set; } = new();

    /// <summary>Weapon and spell attacks.</summary>
    public List<Attack> Attacks { get; set; } = [];

    /// <summary>Spellcasting information, slots, and known/prepared spells.</summary>
    public SpellcastingInfo? Spells { get; set; }

    /// <summary>Class features, racial traits, feats, and other special abilities.</summary>
    public List<Feature> Features { get; set; } = [];

    /// <summary>Equipment and carried items.</summary>
    public List<InventoryItem> Inventory { get; set; } = [];

    /// <summary>Limited-use resources such as Ki points, Bardic Inspiration, Channel Divinity.</summary>
    public List<Resource> Resources { get; set; } = [];

    /// <summary>Coin purse.</summary>
    public Currency Currency { get; set; } = new();

    /// <summary>Free-form notes visible to the character owner.</summary>
    public string? Notes { get; set; }
}

// ── Identity ─────────────────────────────────────────────────────────────────

/// <summary>Biographical and descriptive information about the character.</summary>
public sealed class CharacterIdentity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Race in D&amp;D 5e or Ancestry in Pathfinder 2e.</summary>
    public string? RaceOrAncestry { get; set; }

    public string? Background { get; set; }
    public string? Alignment { get; set; }
    public int? Age { get; set; }
    public string? Height { get; set; }
    public string? Weight { get; set; }
    public string? Appearance { get; set; }
    public string? Backstory { get; set; }
    public string? PersonalityTraits { get; set; }
    public string? Ideals { get; set; }
    public string? Bonds { get; set; }
    public string? Flaws { get; set; }
}

// ── Classes ───────────────────────────────────────────────────────────────────

/// <summary>A single class entry, supporting multiclassing.</summary>
public sealed class CharacterClass
{
    public string ClassName { get; set; } = string.Empty;
    public int Level { get; set; }
    public string? Subclass { get; set; }

    /// <summary>Hit die size, e.g. 6, 8, 10, or 12.</summary>
    public int HitDie { get; set; }
}

// ── Abilities ─────────────────────────────────────────────────────────────────

/// <summary>The six core ability scores (1–30).</summary>
public sealed class AbilityScores
{
    public int Strength { get; set; } = 10;
    public int Dexterity { get; set; } = 10;
    public int Constitution { get; set; } = 10;
    public int Intelligence { get; set; } = 10;
    public int Wisdom { get; set; } = 10;
    public int Charisma { get; set; } = 10;

    /// <summary>Returns the standard 5e ability modifier for a given score.</summary>
    public static int ModifierFor(int score) => (int)Math.Floor((score - 10) / 2.0);

    public int StrengthModifier => ModifierFor(Strength);
    public int DexterityModifier => ModifierFor(Dexterity);
    public int ConstitutionModifier => ModifierFor(Constitution);
    public int IntelligenceModifier => ModifierFor(Intelligence);
    public int WisdomModifier => ModifierFor(Wisdom);
    public int CharismaModifier => ModifierFor(Charisma);
}

// ── Saving Throws ─────────────────────────────────────────────────────────────

/// <summary>Proficiency flags for each saving throw.</summary>
public sealed class SavingThrows
{
    public bool Strength { get; set; }
    public bool Dexterity { get; set; }
    public bool Constitution { get; set; }
    public bool Intelligence { get; set; }
    public bool Wisdom { get; set; }
    public bool Charisma { get; set; }
}

// ── Skills ────────────────────────────────────────────────────────────────────

/// <summary>Proficiency level for a single skill.</summary>
public sealed class SkillProficiency
{
    public string SkillName { get; set; } = string.Empty;
    public ProficiencyLevel ProficiencyLevel { get; set; } = ProficiencyLevel.None;
}

/// <summary>Degree of proficiency in a skill or saving throw.</summary>
public enum ProficiencyLevel
{
    None = 0,
    HalfProficient = 1,
    Proficient = 2,
    Expert = 3
}

// ── Combat ────────────────────────────────────────────────────────────────────

/// <summary>Core combat statistics tracked on the character sheet.</summary>
public sealed class CombatStats
{
    public int ArmorClass { get; set; } = 10;
    public int Initiative { get; set; }
    public int Speed { get; set; } = 30;
    public int MaxHitPoints { get; set; }
    public int CurrentHitPoints { get; set; }
    public int TemporaryHitPoints { get; set; }

    /// <summary>Remaining hit dice, e.g. "3d10 2d6".</summary>
    public string? HitDice { get; set; }

    public int ProficiencyBonus { get; set; } = 2;
    public bool InspirationDie { get; set; }
}

// ── Death Saves ───────────────────────────────────────────────────────────────

/// <summary>Death saving throw counters.</summary>
public sealed class DeathSaves
{
    public int Successes { get; set; }
    public int Failures { get; set; }
}

// ── Attacks ───────────────────────────────────────────────────────────────────

/// <summary>A single attack entry on the character sheet.</summary>
public sealed class Attack
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Attack bonus as a signed integer, e.g. +5.</summary>
    public int? AttackBonus { get; set; }

    /// <summary>Damage expression, e.g. "1d8+3".</summary>
    public string? Damage { get; set; }

    public string? DamageType { get; set; }

    /// <summary>Range description, e.g. "5 ft." or "80/320 ft."</summary>
    public string? Range { get; set; }

    /// <summary>Weapon properties, e.g. ["Finesse", "Light"].</summary>
    public List<string> Properties { get; set; } = [];
}

// ── Spells ────────────────────────────────────────────────────────────────────

/// <summary>All spellcasting information for the character.</summary>
public sealed class SpellcastingInfo
{
    public string? SpellcastingAbility { get; set; }
    public int SpellSaveDc { get; set; }
    public int SpellAttackBonus { get; set; }

    /// <summary>Spell slots by level (1–9).</summary>
    public List<SpellSlot> SpellSlots { get; set; } = [];

    /// <summary>Spells the character knows or has prepared.</summary>
    public List<KnownSpell> KnownSpells { get; set; } = [];
}

/// <summary>Spell slot pool for a given spell level.</summary>
public sealed class SpellSlot
{
    /// <summary>Spell level 1–9.</summary>
    public int Level { get; set; }
    public int Total { get; set; }
    public int Used { get; set; }
    public int Remaining => Math.Max(0, Total - Used);
}

/// <summary>A single spell entry.</summary>
public sealed class KnownSpell
{
    public string Name { get; set; } = string.Empty;

    /// <summary>0 for cantrips, 1–9 for levelled spells.</summary>
    public int Level { get; set; }

    public string? School { get; set; }
    public bool Prepared { get; set; }
    public bool Ritual { get; set; }
    public bool Concentration { get; set; }
    public string? CastingTime { get; set; }
    public string? Range { get; set; }
    public string? Duration { get; set; }
    public string? Description { get; set; }
}

// ── Features ──────────────────────────────────────────────────────────────────

/// <summary>A class feature, racial trait, feat, or other special ability.</summary>
public sealed class Feature
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Source of the feature, e.g. "Fighter 1", "Human", "Alert".</summary>
    public string? Source { get; set; }

    public string? Description { get; set; }
}

// ── Inventory ─────────────────────────────────────────────────────────────────

/// <summary>A single item in the character's inventory.</summary>
public sealed class InventoryItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;

    /// <summary>Weight in pounds (or system-appropriate unit).</summary>
    public decimal? Weight { get; set; }

    public string? Description { get; set; }
    public bool Equipped { get; set; }
    public bool Attuned { get; set; }
}

// ── Resources ─────────────────────────────────────────────────────────────────

/// <summary>A limited-use resource such as Ki points, Bardic Inspiration, or Channel Divinity.</summary>
public sealed class Resource
{
    public string Name { get; set; } = string.Empty;
    public int Current { get; set; }
    public int Maximum { get; set; }

    /// <summary>When the resource recharges, e.g. "Short Rest", "Long Rest", "Dawn".</summary>
    public string? RechargeOn { get; set; }
}

// ── Currency ──────────────────────────────────────────────────────────────────

/// <summary>Standard coin purse.</summary>
public sealed class Currency
{
    public int Copper { get; set; }
    public int Silver { get; set; }
    public int Electrum { get; set; }
    public int Gold { get; set; }
    public int Platinum { get; set; }
}
