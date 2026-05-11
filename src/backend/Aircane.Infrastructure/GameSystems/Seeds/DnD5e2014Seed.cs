using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Infrastructure.GameSystems.Seeds;

/// <summary>
/// Produces the built-in Game System Definition for D&D 5e 2014.
/// This seed matches the current hardcoded D&D 5e behavior in Aircane.
/// </summary>
public static class DnD5e2014Seed
{
    /// <summary>
    /// Well-known ID for the built-in D&D 5e 2014 definition.
    /// Using a deterministic GUID so migrations can reference it reliably.
    /// </summary>
    public static readonly Guid DefinitionId = new("10000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Creates the complete D&D 5e 2014 Game System Definition.
    /// </summary>
    public static GameSystemDefinition Create()
    {
        var definition = new GameSystemDefinition(
            identifier: "dnd-5e-2014",
            name: "Dungeons & Dragons 5th Edition (2014)",
            version: "1.0.0",
            schemaVersion: 1,
            license: "built-in",
            publisher: "Wizards of the Coast",
            genre: "fantasy",
            description: "The 2014 core rules for D&D 5th Edition. Uses a d20+modifier system with advantage/disadvantage, six ability scores, and class-based character progression.")
        {
            IsBuiltIn = true,
            IsActive = true,
            Tags = new List<string> { "d20", "fantasy", "levels", "classes" },
            DiceConventions = CreateDiceConventions(),
            ResolutionRules = CreateResolutionRules(),
            CharacterSchema = CreateCharacterSchema(),
            ConditionSet = CreateConditionSet(),
            ActionEconomy = CreateActionEconomy(),
            EncounterBudget = CreateEncounterBudget(),
            AiGuidance = CreateAiGuidance()
        };

        // Set the well-known ID via reflection on the init-only property
        // (EntityBase generates a random one in the constructor)
        SetId(definition, DefinitionId);

        return definition;
    }

    private static void SetId(GameSystemDefinition definition, Guid id)
    {
        var prop = typeof(GameSystemDefinition).BaseType!.GetProperty("Id")!;
        prop.SetValue(definition, id);
    }

    private static IReadOnlyList<DiceConvention> CreateDiceConventions() =>
    [
        new DiceConvention
        {
            Name = "primary",
            Type = DiceConventionType.SingleDieModifier,
            Die = "d20",
            Description = "Primary d20 roll with ability modifier and proficiency bonus",
            ModifierSources = new List<string> { "ability_modifier", "proficiency_bonus" },
            Advantage = new KeepDirective { Roll = 2, Keep = "highest" },
            Disadvantage = new KeepDirective { Roll = 2, Keep = "lowest" }
        },
        new DiceConvention
        {
            Name = "damage",
            Type = DiceConventionType.Expression,
            Description = "Variable dice + modifier for damage rolls"
        },
        new DiceConvention
        {
            Name = "hit_dice",
            Type = DiceConventionType.Expression,
            Description = "Class-specific hit dice for healing during rests"
        }
    ];

    private static IReadOnlyList<ResolutionRule> CreateResolutionRules() =>
    [
        new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        },
        new ResolutionRule
        {
            Name = "attackRoll",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "ac",
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        },
        new ResolutionRule
        {
            Name = "savingThrow",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        }
    ];

    private static CharacterSchema CreateCharacterSchema() => new()
    {
        Sections = new List<CharacterSchemaSection>
        {
            new()
            {
                Id = "basics",
                Label = "Basic Information",
                Fields = new List<CharacterSchemaField>
                {
                    new() { Id = "name", Type = CharacterFieldType.Text, Label = "Character Name", Required = true },
                    new() { Id = "level", Type = CharacterFieldType.Number, Label = "Level", Required = true, Min = 1, Max = 20 },
                    new() { Id = "class", Type = CharacterFieldType.Enum, Label = "Class", Options = new List<string>
                        { "Barbarian", "Bard", "Cleric", "Druid", "Fighter", "Monk", "Paladin", "Ranger", "Rogue", "Sorcerer", "Warlock", "Wizard" } },
                    new() { Id = "race", Type = CharacterFieldType.Text, Label = "Race" }
                }
            },
            new()
            {
                Id = "abilities",
                Label = "Ability Scores",
                Fields = new List<CharacterSchemaField>
                {
                    new() { Id = "str", Type = CharacterFieldType.Number, Label = "Strength", Min = 1, Max = 30 },
                    new() { Id = "str_mod", Type = CharacterFieldType.Calculated, Label = "STR Mod", Formula = "floor((str - 10) / 2)" },
                    new() { Id = "dex", Type = CharacterFieldType.Number, Label = "Dexterity", Min = 1, Max = 30 },
                    new() { Id = "dex_mod", Type = CharacterFieldType.Calculated, Label = "DEX Mod", Formula = "floor((dex - 10) / 2)" },
                    new() { Id = "con", Type = CharacterFieldType.Number, Label = "Constitution", Min = 1, Max = 30 },
                    new() { Id = "con_mod", Type = CharacterFieldType.Calculated, Label = "CON Mod", Formula = "floor((con - 10) / 2)" },
                    new() { Id = "int", Type = CharacterFieldType.Number, Label = "Intelligence", Min = 1, Max = 30 },
                    new() { Id = "int_mod", Type = CharacterFieldType.Calculated, Label = "INT Mod", Formula = "floor((int - 10) / 2)" },
                    new() { Id = "wis", Type = CharacterFieldType.Number, Label = "Wisdom", Min = 1, Max = 30 },
                    new() { Id = "wis_mod", Type = CharacterFieldType.Calculated, Label = "WIS Mod", Formula = "floor((wis - 10) / 2)" },
                    new() { Id = "cha", Type = CharacterFieldType.Number, Label = "Charisma", Min = 1, Max = 30 },
                    new() { Id = "cha_mod", Type = CharacterFieldType.Calculated, Label = "CHA Mod", Formula = "floor((cha - 10) / 2)" }
                }
            },
            new()
            {
                Id = "combat",
                Label = "Combat",
                Fields = new List<CharacterSchemaField>
                {
                    new() { Id = "ac", Type = CharacterFieldType.Number, Label = "Armor Class" },
                    new() { Id = "hp_max", Type = CharacterFieldType.Number, Label = "Max HP" },
                    new() { Id = "hp_current", Type = CharacterFieldType.ResourcePool, Label = "Hit Points", MaxField = "hp_max" },
                    new() { Id = "initiative", Type = CharacterFieldType.Calculated, Label = "Initiative", Formula = "floor((dex - 10) / 2)" }
                }
            },
            new()
            {
                Id = "spells",
                Label = "Spellcasting",
                VisibleWhen = new VisibilityCondition
                {
                    Field = "class",
                    In = new List<string> { "Bard", "Cleric", "Druid", "Paladin", "Ranger", "Sorcerer", "Warlock", "Wizard" }
                },
                Fields = new List<CharacterSchemaField>
                {
                    new() { Id = "spell_slots_1_max", Type = CharacterFieldType.Number, Label = "1st Level Slots (Max)" },
                    new() { Id = "spell_slots_1", Type = CharacterFieldType.ResourcePool, Label = "1st Level Slots", MaxField = "spell_slots_1_max" },
                    new() { Id = "spell_slots_2_max", Type = CharacterFieldType.Number, Label = "2nd Level Slots (Max)" },
                    new() { Id = "spell_slots_2", Type = CharacterFieldType.ResourcePool, Label = "2nd Level Slots", MaxField = "spell_slots_2_max" },
                    new() { Id = "spell_slots_3_max", Type = CharacterFieldType.Number, Label = "3rd Level Slots (Max)" },
                    new() { Id = "spell_slots_3", Type = CharacterFieldType.ResourcePool, Label = "3rd Level Slots", MaxField = "spell_slots_3_max" },
                    new() { Id = "known_spells", Type = CharacterFieldType.Repeating, Label = "Known Spells",
                        ItemSchema = new Dictionary<string, string> { ["name"] = "text", ["level"] = "number", ["school"] = "text" } }
                }
            }
        }
    };

    private static IReadOnlyList<ConditionDefinition> CreateConditionSet() =>
    [
        new ConditionDefinition
        {
            Name = "Blinded",
            Description = "Can't see. Attack rolls against have advantage, own attack rolls have disadvantage.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new() { Type = "grant_advantage", Scope = "attacks_against" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Charmed",
            Description = "Can't attack the charmer. Charmer has advantage on social checks.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "grant_advantage", Scope = "social_checks_by_charmer" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Deafened",
            Description = "Can't hear. Automatically fails ability checks that require hearing.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "auto_fail", Scope = "hearing_checks" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Frightened",
            Description = "Disadvantage on ability checks and attack rolls while source of fear is in line of sight.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "roll_modifier", Scope = "ability_checks", Effect = "disadvantage" },
                new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Grappled",
            Description = "Speed becomes 0.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "set_value", Scope = "speed", Effect = "0" }
            },
            DurationType = "until_action",
            EndCondition = "Escape with Athletics or Acrobatics check vs grappler's Athletics",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Incapacitated",
            Description = "Can't take actions or reactions.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "prevent_action", Scope = "reactions" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Invisible",
            Description = "Impossible to see without special sense. Attack rolls against have disadvantage, own attack rolls have advantage.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "advantage" },
                new() { Type = "grant_disadvantage", Scope = "attacks_against" }
            },
            DurationType = "rounds",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Paralyzed",
            Description = "Incapacitated, can't move or speak. Auto-fail STR/DEX saves. Attacks have advantage, melee hits are crits.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "prevent_action", Scope = "movement" },
                new() { Type = "auto_fail", Scope = "str_saves" },
                new() { Type = "auto_fail", Scope = "dex_saves" },
                new() { Type = "grant_advantage", Scope = "attacks_against" },
                new() { Type = "grant_critical", Scope = "melee_attacks_against" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Petrified",
            Description = "Transformed to inanimate substance. Weight increases x10. Incapacitated, can't move or speak.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "prevent_action", Scope = "movement" },
                new() { Type = "damage_resistance", Scope = "all_damage" },
                new() { Type = "grant_advantage", Scope = "attacks_against" },
                new() { Type = "auto_fail", Scope = "str_saves" },
                new() { Type = "auto_fail", Scope = "dex_saves" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Poisoned",
            Description = "Disadvantage on attack rolls and ability checks.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new() { Type = "roll_modifier", Scope = "ability_checks", Effect = "disadvantage" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Prone",
            Description = "Disadvantage on attack rolls. Melee attacks against have advantage, ranged attacks have disadvantage.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new() { Type = "grant_advantage", Scope = "melee_attacks_against" },
                new() { Type = "grant_disadvantage", Scope = "ranged_attacks_against" }
            },
            DurationType = "until_action",
            EndCondition = "Use half movement to stand",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Restrained",
            Description = "Speed becomes 0. Attack rolls have disadvantage. DEX saves have disadvantage. Attacks against have advantage.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "set_value", Scope = "speed", Effect = "0" },
                new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new() { Type = "roll_modifier", Scope = "dex_saves", Effect = "disadvantage" },
                new() { Type = "grant_advantage", Scope = "attacks_against" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Stunned",
            Description = "Incapacitated, can't move, can speak only falteringly. Auto-fail STR/DEX saves. Attacks against have advantage.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "auto_fail", Scope = "str_saves" },
                new() { Type = "auto_fail", Scope = "dex_saves" },
                new() { Type = "grant_advantage", Scope = "attacks_against" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Unconscious",
            Description = "Incapacitated, can't move or speak, unaware of surroundings. Drop what held, fall prone. Auto-fail STR/DEX saves. Attacks have advantage, melee hits are crits.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "prevent_action", Scope = "movement" },
                new() { Type = "auto_fail", Scope = "str_saves" },
                new() { Type = "auto_fail", Scope = "dex_saves" },
                new() { Type = "grant_advantage", Scope = "attacks_against" },
                new() { Type = "grant_critical", Scope = "melee_attacks_against" }
            },
            DurationType = "until_save",
            Stackable = false
        }
    ];

    private static ActionEconomyDefinition CreateActionEconomy() => new()
    {
        Type = ActionEconomyType.NamedSlots,
        TurnStructure = new TurnStructure
        {
            Slots = new List<ActionSlot>
            {
                new() { Name = "action", Label = "Action", Count = 1 },
                new() { Name = "bonus_action", Label = "Bonus Action", Count = 1 },
                new() { Name = "reaction", Label = "Reaction", Count = 1, ResetOn = "turn_start" },
                new() { Name = "movement", Label = "Movement", Count = 1, Resource = "speed" },
                new() { Name = "free_action", Label = "Free Action", Count = -1 }
            }
        }
    };

    private static EncounterBudgetFormula CreateEncounterBudget() => new()
    {
        Type = EncounterBudgetType.XpBudget,
        DifficultyTiers = new List<DifficultyTier>
        {
            new() { Name = "Easy", Multiplier = 0.5 },
            new() { Name = "Medium", Multiplier = 1.0 },
            new() { Name = "Hard", Multiplier = 1.5 },
            new() { Name = "Deadly", Multiplier = 2.0 }
        },
        Formula = "sum(character_xp_threshold[level][difficulty]) * party_size_modifier",
        CreatureCostField = "xp"
    };

    private static AiGuidance CreateAiGuidance() => new()
    {
        SystemPromptNotes = "This is a d20-based fantasy RPG. The core mechanic is rolling a d20, adding modifiers, and comparing against a target number (DC or AC). Use descriptive narration for combat and exploration. Characters have six ability scores (Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma) that provide modifiers to rolls.",
        ToneGuidance = "High fantasy, heroic, dramatic combat descriptions. Balance tactical combat with narrative exploration and social encounters.",
        MechanicalNotes = "Always ask for ability checks using d20+modifier vs DC. Attack rolls are d20+modifier vs AC. Natural 20 on attack rolls is always a critical hit (double damage dice). Natural 1 on attack rolls is always a miss. Saving throws are d20+modifier vs DC. Advantage means roll 2d20 take highest. Disadvantage means roll 2d20 take lowest.",
        CommonMistakes = new List<string>
        {
            "Do not use degrees of success — this system uses binary pass/fail with critical hits only on natural 20.",
            "Do not allow stacking of advantage/disadvantage — they cancel each other out regardless of how many sources.",
            "Do not forget concentration checks when a spellcaster takes damage.",
            "Do not apply ability score modifiers directly — always use the modifier (floor((score-10)/2))."
        },
        RollFormatExample = "1d20+{ability_modifier}+{proficiency_bonus}"
    };
}
