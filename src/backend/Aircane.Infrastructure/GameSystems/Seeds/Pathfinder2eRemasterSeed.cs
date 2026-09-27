using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Infrastructure.GameSystems.Seeds;

/// <summary>
/// Produces the built-in Game System Definition for Pathfinder 2e (Remaster).
/// <para>
/// This is a mechanics DEFINITION (dice conventions, degrees of success, three-action economy,
/// conditions, encounter budget, AI guidance) — it is NOT the ORC-licensed rules text bundle,
/// which is sourced separately. Only game mechanics are represented here; no Product Identity
/// (proper nouns, iconic characters, setting names) is included.
/// </para>
/// </summary>
public static class Pathfinder2eRemasterSeed
{
    /// <summary>Well-known deterministic ID for the built-in PF2e Remaster definition.</summary>
    public static readonly Guid DefinitionId = new("10000000-0000-0000-0000-000000000003");

    /// <summary>Creates the complete Pathfinder 2e (Remaster) Game System Definition.</summary>
    public static GameSystemDefinition Create()
    {
        var definition = new GameSystemDefinition(
            identifier: "pathfinder-2e-remaster",
            name: "Pathfinder Second Edition (Remaster)",
            version: "1.0.0",
            schemaVersion: 1,
            license: "built-in",
            publisher: "Paizo Inc.",
            genre: "fantasy",
            description: "The Pathfinder Second Edition Remaster ruleset. A d20 system built on four degrees of success (beat or miss the DC by 10), a three-action economy with a multiple attack penalty, and proficiency-scaled modifiers.")
        {
            IsBuiltIn = true,
            IsActive = true,
            Tags = new List<string> { "d20", "fantasy", "levels", "classes", "degrees-of-success", "three-action" },
            DiceConventions = CreateDiceConventions(),
            ResolutionRules = CreateResolutionRules(),
            CharacterSchema = CreateCharacterSchema(),
            ConditionSet = CreateConditionSet(),
            ActionEconomy = CreateActionEconomy(),
            EncounterBudget = CreateEncounterBudget(),
            AiGuidance = CreateAiGuidance()
        };

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
            Description = "Primary d20 roll plus ability modifier, proficiency bonus (proficiency rank + level), and item/circumstance/status bonuses.",
            ModifierSources = new List<string> { "ability_modifier", "proficiency_bonus", "item_bonus", "circumstance_bonus", "status_bonus" }
        },
        new DiceConvention
        {
            Name = "damage",
            Type = DiceConventionType.Expression,
            Description = "Weapon/spell dice plus modifiers; doubled dice+modifiers on a critical hit."
        }
    ];

    private static IReadOnlyList<ResolutionRule> CreateResolutionRules() =>
    [
        // PF2e's signature four degrees of success: beat the DC by 10 = critical success,
        // meet/beat = success, miss = failure, miss by 10 = critical failure. A natural 20
        // improves the degree by one step and a natural 1 worsens it by one step.
        new ResolutionRule
        {
            Name = "check",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            TargetSource = "dc",
            DegreesOfSuccess = DegreesRelativeToDc(),
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        },
        new ResolutionRule
        {
            Name = "attack",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            TargetSource = "ac",
            DegreesOfSuccess = DegreesRelativeToDc(),
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        },
        new ResolutionRule
        {
            Name = "savingThrow",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            TargetSource = "dc",
            DegreesOfSuccess = DegreesRelativeToDc(),
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        }
    ];

    /// <summary>
    /// Degree bands expressed as the margin (roll total - DC): +10 or more is a critical success,
    /// 0..9 success, -1..-9 failure, -10 or worse critical failure.
    /// </summary>
    private static IReadOnlyList<DegreeThreshold> DegreesRelativeToDc() =>
    [
        new DegreeThreshold { Name = "Critical Failure", MaxValue = -10 },
        new DegreeThreshold { Name = "Failure", MinValue = -9, MaxValue = -1 },
        new DegreeThreshold { Name = "Success", MinValue = 0, MaxValue = 9 },
        new DegreeThreshold { Name = "Critical Success", MinValue = 10 }
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
                    new() { Id = "ancestry", Type = CharacterFieldType.Text, Label = "Ancestry" },
                    new() { Id = "heritage", Type = CharacterFieldType.Text, Label = "Heritage" },
                    new() { Id = "background", Type = CharacterFieldType.Text, Label = "Background" },
                    new() { Id = "class", Type = CharacterFieldType.Text, Label = "Class" }
                }
            },
            new()
            {
                Id = "abilities",
                Label = "Ability Modifiers",
                Fields = new List<CharacterSchemaField>
                {
                    // PF2e sheets track ability modifiers directly rather than raw scores.
                    new() { Id = "str", Type = CharacterFieldType.Number, Label = "Strength Mod", Min = -5, Max = 10 },
                    new() { Id = "dex", Type = CharacterFieldType.Number, Label = "Dexterity Mod", Min = -5, Max = 10 },
                    new() { Id = "con", Type = CharacterFieldType.Number, Label = "Constitution Mod", Min = -5, Max = 10 },
                    new() { Id = "int", Type = CharacterFieldType.Number, Label = "Intelligence Mod", Min = -5, Max = 10 },
                    new() { Id = "wis", Type = CharacterFieldType.Number, Label = "Wisdom Mod", Min = -5, Max = 10 },
                    new() { Id = "cha", Type = CharacterFieldType.Number, Label = "Charisma Mod", Min = -5, Max = 10 }
                }
            },
            new()
            {
                Id = "defenses",
                Label = "Defenses",
                Fields = new List<CharacterSchemaField>
                {
                    new() { Id = "ac", Type = CharacterFieldType.Number, Label = "Armor Class" },
                    new() { Id = "hp_max", Type = CharacterFieldType.Number, Label = "Max HP" },
                    new() { Id = "hp_current", Type = CharacterFieldType.ResourcePool, Label = "Hit Points", MaxField = "hp_max" },
                    new() { Id = "fortitude", Type = CharacterFieldType.Number, Label = "Fortitude Save" },
                    new() { Id = "reflex", Type = CharacterFieldType.Number, Label = "Reflex Save" },
                    new() { Id = "will", Type = CharacterFieldType.Number, Label = "Will Save" }
                }
            },
            new()
            {
                Id = "proficiencies",
                Label = "Proficiencies",
                Fields = new List<CharacterSchemaField>
                {
                    // PF2e proficiency ranks; the rank adds level + a fixed bonus (2/4/6/8) to relevant rolls.
                    new() { Id = "perception_rank", Type = CharacterFieldType.Enum, Label = "Perception",
                        Options = new List<string> { "Untrained", "Trained", "Expert", "Master", "Legendary" } },
                    new() { Id = "class_dc_rank", Type = CharacterFieldType.Enum, Label = "Class DC",
                        Options = new List<string> { "Untrained", "Trained", "Expert", "Master", "Legendary" } }
                }
            }
        }
    };

    private static IReadOnlyList<ConditionDefinition> CreateConditionSet() =>
    [
        new ConditionDefinition
        {
            Name = "Clumsy",
            Description = "Take a status penalty equal to the condition value to Dexterity-based checks and DCs, including AC, Reflex saves, ranged attack rolls, and Dexterity-based skill checks.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "status_penalty", Scope = "dex_based", Effect = "value" }
            },
            DurationType = "value",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Dying",
            Description = "Near death and unconscious. Increases with damage while dying; at dying 4 the creature dies. Reduced by the Recovery check each turn.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "unconscious", Scope = "self" }
            },
            DurationType = "value",
            EndCondition = "Roll a recovery check each turn; reaching Dying 4 is death, reaching Dying 0 stabilizes.",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Enfeebled",
            Description = "Weakened; take a status penalty equal to the condition value to Strength-based checks and DCs, including melee attack rolls and Strength-based skills.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "status_penalty", Scope = "str_based", Effect = "value" }
            },
            DurationType = "value",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Fascinated",
            Description = "Compelled to focus attention on something. Take a -2 status penalty to Perception and skill checks, and cannot use concentrate actions unrelated to the subject.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "status_penalty", Scope = "perception", Effect = "-2" },
                new() { Type = "status_penalty", Scope = "skill_checks", Effect = "-2" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Frightened",
            Description = "Afraid; take a status penalty equal to the condition value to all checks and DCs. The value decreases by 1 at the end of each turn.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "status_penalty", Scope = "all_checks", Effect = "value" }
            },
            DurationType = "value",
            EndCondition = "Decreases by 1 at the end of each turn.",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Grabbed",
            Description = "Held in place by another creature. Flat-footed and cannot use manipulate actions unless it succeeds a check.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "flat_footed", Scope = "self" },
                new() { Type = "restrict_action", Scope = "manipulate" }
            },
            DurationType = "until_action",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Off-Guard",
            Description = "Distracted or otherwise unable to focus fully on defense. Take a -2 circumstance penalty to AC.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "circumstance_penalty", Scope = "ac", Effect = "-2" }
            },
            DurationType = "until_action",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Prone",
            Description = "Lying on the ground. Off-guard and take a -2 circumstance penalty to attack rolls. Must Crawl or Stand to move.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "circumstance_penalty", Scope = "attack_rolls", Effect = "-2" },
                new() { Type = "flat_footed", Scope = "self" }
            },
            DurationType = "until_action",
            EndCondition = "Spend an action to Stand.",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Quickened",
            Description = "Gain 1 additional action at the start of your turn each round.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "grant_action", Scope = "self", Effect = "1" }
            },
            DurationType = "rounds",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Sickened",
            Description = "Take a status penalty equal to the condition value to all checks and DCs, and cannot willingly ingest anything. Retch to reduce the value.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "status_penalty", Scope = "all_checks", Effect = "value" }
            },
            DurationType = "value",
            EndCondition = "Spend an action to Retch and attempt a Fortitude save to reduce the value.",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Slowed",
            Description = "Lose a number of actions at the start of your turn equal to the condition value.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "lose_action", Scope = "self", Effect = "value" }
            },
            DurationType = "value",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Stunned",
            Description = "Lose actions equal to the condition value (or lose all actions for a set duration). Reduces slowed but not vice versa.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "lose_action", Scope = "self", Effect = "value" }
            },
            DurationType = "value",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Unconscious",
            Description = "Asleep or knocked out. Off-guard, cannot act, and take a -4 status penalty to AC, Perception, and Reflex saves.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "prevent_action", Scope = "actions" },
                new() { Type = "flat_footed", Scope = "self" },
                new() { Type = "status_penalty", Scope = "ac", Effect = "-4" }
            },
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Wounded",
            Description = "When you lose the dying condition, you become wounded. If you gain dying again, its value increases by your wounded value.",
            Effects = new List<ConditionEffect>
            {
                new() { Type = "modify_dying", Scope = "self", Effect = "value" }
            },
            DurationType = "until_rest",
            EndCondition = "Removed by receiving sufficient healing over time or treatment.",
            Stackable = false
        }
    ];

    private static ActionEconomyDefinition CreateActionEconomy() => new()
    {
        // PF2e's hallmark: three single actions per turn plus one reaction, with a multiple
        // attack penalty applied to attacks after the first.
        Type = ActionEconomyType.MultiActionPenalty,
        MaxActions = 3,
        PenaltyIncrement = -5,
        TurnStructure = new TurnStructure
        {
            Slots = new List<ActionSlot>
            {
                new() { Name = "action", Label = "Action", Count = 3 },
                new() { Name = "reaction", Label = "Reaction", Count = 1, ResetOn = "turn_start" },
                new() { Name = "free_action", Label = "Free Action", Count = -1 }
            }
        }
    };

    private static EncounterBudgetFormula CreateEncounterBudget() => new()
    {
        // PF2e uses a creature-level XP budget: each foe's XP is set by its level relative to
        // the party level, summed against a threat threshold.
        Type = EncounterBudgetType.CreatureLevel,
        DifficultyTiers = new List<DifficultyTier>
        {
            new() { Name = "Trivial", Multiplier = 40 },
            new() { Name = "Low", Multiplier = 60 },
            new() { Name = "Moderate", Multiplier = 80 },
            new() { Name = "Severe", Multiplier = 120 },
            new() { Name = "Extreme", Multiplier = 160 }
        },
        Formula = "sum(creature_xp_by_level_difference) vs threat_threshold[party_size]",
        CreatureCostField = "xp"
    };

    private static AiGuidance CreateAiGuidance() => new()
    {
        SystemPromptNotes = "This is Pathfinder Second Edition (Remaster), a d20 system built on four degrees of success. Roll d20 + modifiers vs a DC: beat the DC by 10 or more for a critical success, meet or beat it for a success, miss for a failure, and miss by 10 or more for a critical failure. A natural 20 improves the result one degree; a natural 1 worsens it one degree. Characters act with three actions and one reaction per turn.",
        ToneGuidance = "Tactical heroic fantasy. Combat is a precise, grid-oriented tactical puzzle; emphasize positioning, action economy, and the consequences of each degree of success.",
        MechanicalNotes = "All checks use degrees of success (critical success / success / failure / critical failure). Attacks after the first on a turn take a multiple attack penalty of -5 (or -4 with an agile weapon), and -10 on the third (-8 agile). Proficiency adds the character's level plus a rank bonus (Trained +2, Expert +4, Master +6, Legendary +8). Critical hits deal double damage (double the dice and modifiers, not the final total). Elite creatures gain a static adjustment (roughly +2 to most numbers and increased HP); weak creatures take the reverse adjustment.",
        CommonMistakes = new List<string>
        {
            "Do not resolve checks as binary pass/fail — always determine the degree of success (crit success/success/failure/crit failure).",
            "Do not use D&D 5e advantage/disadvantage — PF2e uses numeric circumstance, status, and item bonuses/penalties that stack by type (only the highest of each type applies).",
            "Do not forget the multiple attack penalty on second and third attacks in a turn.",
            "Do not apply more than one bonus (or penalty) of the same type — take the highest bonus and the worst penalty of each type.",
            "Do not treat 'off-guard' as advantage; it is a flat -2 circumstance penalty to AC."
        },
        RollFormatExample = "1d20+{ability_modifier}+{proficiency_bonus}+{item_bonus}"
    };
}
