using Aircane.Application.DTOs.GameSystems;

namespace Aircane.Infrastructure.GameSystems.Seeds;

/// <summary>
/// Provides starter templates for common TTRPG archetypes.
/// Each template contains a valid GameSystemDefinition JSON that passes validation
/// and can be used as a starting point for creating custom game system definitions.
/// </summary>
public static class StarterTemplates
{
    /// <summary>
    /// Returns all available starter templates.
    /// </summary>
    public static IReadOnlyList<GameSystemTemplateDto> GetAll() =>
    [
        D20System(),
        DicePool(),
        PbtA(),
        Percentile(),
        Freeform()
    ];

    /// <summary>
    /// d20 System template - classic d20 + modifier vs target number (D&D, Pathfinder).
    /// </summary>
    public static GameSystemTemplateDto D20System() => new(
        Id: "d20-system",
        Name: "d20 System",
        Description: "Classic d20 + modifier vs target number (D&D, Pathfinder)",
        Genre: "fantasy",
        DefinitionJson: """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "my-d20-system",
                "name": "My d20 System",
                "version": "1.0.0",
                "genre": "fantasy",
                "license": "user-created"
            },
            "diceConventions": {
                "primary": {
                    "type": "single_die_modifier",
                    "die": "d20",
                    "modifier_sources": ["ability_modifier", "proficiency_bonus"],
                    "advantage": { "roll": 2, "keep": "highest" },
                    "disadvantage": { "roll": 2, "keep": "lowest" }
                }
            },
            "resolutionRules": {
                "abilityCheck": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": ">=",
                    "target_source": "dc"
                },
                "attackRoll": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": ">=",
                    "target_source": "ac",
                    "critical_success": { "natural_roll": 20 },
                    "critical_failure": { "natural_roll": 1 }
                }
            },
            "characterSchema": {
                "sections": [
                    {
                        "id": "basics",
                        "label": "Basic Information",
                        "fields": [
                            { "id": "name", "type": "text", "label": "Character Name", "required": true },
                            { "id": "level", "type": "number", "label": "Level", "required": true, "min": 1, "max": 20 },
                            { "id": "class", "type": "text", "label": "Class" },
                            { "id": "race", "type": "text", "label": "Race" }
                        ]
                    },
                    {
                        "id": "abilities",
                        "label": "Ability Scores",
                        "fields": [
                            { "id": "str", "type": "number", "label": "Strength", "min": 1, "max": 30 },
                            { "id": "dex", "type": "number", "label": "Dexterity", "min": 1, "max": 30 },
                            { "id": "con", "type": "number", "label": "Constitution", "min": 1, "max": 30 },
                            { "id": "int", "type": "number", "label": "Intelligence", "min": 1, "max": 30 },
                            { "id": "wis", "type": "number", "label": "Wisdom", "min": 1, "max": 30 },
                            { "id": "cha", "type": "number", "label": "Charisma", "min": 1, "max": 30 }
                        ]
                    },
                    {
                        "id": "combat",
                        "label": "Combat",
                        "fields": [
                            { "id": "ac", "type": "number", "label": "Armor Class" },
                            { "id": "hp_max", "type": "number", "label": "Max HP" },
                            { "id": "hp_current", "type": "resource_pool", "label": "Hit Points", "max_field": "hp_max" }
                        ]
                    }
                ]
            },
            "conditionSet": {
                "conditions": [
                    {
                        "name": "Poisoned",
                        "description": "Disadvantage on attack rolls and ability checks.",
                        "effects": [
                            { "type": "roll_modifier", "scope": "attack_rolls", "effect": "disadvantage" },
                            { "type": "roll_modifier", "scope": "ability_checks", "effect": "disadvantage" }
                        ],
                        "duration_type": "until_save",
                        "stackable": false
                    },
                    {
                        "name": "Prone",
                        "description": "Disadvantage on attack rolls. Melee attacks against have advantage.",
                        "effects": [
                            { "type": "roll_modifier", "scope": "attack_rolls", "effect": "disadvantage" },
                            { "type": "grant_advantage", "scope": "melee_attacks_against" }
                        ],
                        "duration_type": "until_action",
                        "end_condition": "Use half movement to stand",
                        "stackable": false
                    }
                ]
            },
            "actionEconomy": {
                "type": "named_slots",
                "turn_structure": {
                    "slots": [
                        { "name": "action", "count": 1, "label": "Action" },
                        { "name": "bonus_action", "count": 1, "label": "Bonus Action" },
                        { "name": "reaction", "count": 1, "label": "Reaction", "reset_on": "turn_start" },
                        { "name": "movement", "count": 1, "label": "Movement", "resource": "speed" }
                    ]
                }
            },
            "aiGuidance": {
                "system_prompt_notes": "This is a d20-based fantasy RPG. Roll d20 + modifier vs target number.",
                "tone_guidance": "High fantasy, heroic",
                "mechanical_notes": "d20+modifier vs DC. Natural 20 is critical hit. Natural 1 is automatic miss.",
                "roll_format_example": "1d20+{ability_modifier}+{proficiency_bonus}"
            }
        }
        """);

    /// <summary>
    /// Dice Pool template - roll multiple dice, count successes (Shadowrun, World of Darkness).
    /// </summary>
    public static GameSystemTemplateDto DicePool() => new(
        Id: "dice-pool",
        Name: "Dice Pool",
        Description: "Roll multiple dice, count successes (Shadowrun, World of Darkness)",
        Genre: "various",
        DefinitionJson: """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "my-dice-pool-system",
                "name": "My Dice Pool System",
                "version": "1.0.0",
                "genre": "various",
                "license": "user-created"
            },
            "diceConventions": {
                "primary": {
                    "type": "dice_pool_success",
                    "die": "d6",
                    "success_threshold": 5
                }
            },
            "resolutionRules": {
                "standardTest": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": ">=",
                    "target_source": "threshold"
                }
            },
            "characterSchema": {
                "sections": [
                    {
                        "id": "basics",
                        "label": "Basic Information",
                        "fields": [
                            { "id": "name", "type": "text", "label": "Character Name", "required": true },
                            { "id": "concept", "type": "text", "label": "Concept" }
                        ]
                    },
                    {
                        "id": "attributes",
                        "label": "Attributes",
                        "fields": [
                            { "id": "body", "type": "number", "label": "Body", "min": 1, "max": 10 },
                            { "id": "agility", "type": "number", "label": "Agility", "min": 1, "max": 10 },
                            { "id": "reaction", "type": "number", "label": "Reaction", "min": 1, "max": 10 },
                            { "id": "strength", "type": "number", "label": "Strength", "min": 1, "max": 10 },
                            { "id": "willpower", "type": "number", "label": "Willpower", "min": 1, "max": 10 },
                            { "id": "logic", "type": "number", "label": "Logic", "min": 1, "max": 10 }
                        ]
                    }
                ]
            },
            "conditionSet": {
                "conditions": [
                    {
                        "name": "Stunned",
                        "description": "Cannot take actions this round.",
                        "effects": [
                            { "type": "prevent_action", "scope": "actions" }
                        ],
                        "duration_type": "rounds",
                        "stackable": false
                    }
                ]
            },
            "actionEconomy": {
                "type": "action_points",
                "points_per_turn": 3
            },
            "aiGuidance": {
                "system_prompt_notes": "This is a dice pool system. Players roll a number of d6 equal to their attribute + skill. Each die showing 5 or 6 is a success.",
                "tone_guidance": "Gritty, noir, cyberpunk or urban fantasy",
                "mechanical_notes": "Roll Attribute+Skill in d6. Count successes (5+). More successes = better outcome.",
                "roll_format_example": "{pool_size}d6>=5"
            }
        }
        """);

    /// <summary>
    /// PbtA template - 2d6 + stat with threshold bands (miss/weak hit/strong hit).
    /// </summary>
    public static GameSystemTemplateDto PbtA() => new(
        Id: "pbta",
        Name: "Powered by the Apocalypse",
        Description: "2d6 + stat with threshold bands (miss/weak hit/strong hit)",
        Genre: "various",
        DefinitionJson: """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "my-pbta-system",
                "name": "My PbtA System",
                "version": "1.0.0",
                "genre": "various",
                "license": "user-created"
            },
            "diceConventions": {
                "primary": {
                    "type": "fixed_dice_threshold",
                    "die": "d6",
                    "description": "Roll 2d6 + stat modifier"
                }
            },
            "resolutionRules": {
                "move": {
                    "type": "threshold_bands",
                    "roll": "primary",
                    "degrees_of_success": [
                        { "name": "Miss", "max_value": 6 },
                        { "name": "Weak Hit", "min_value": 7, "max_value": 9 },
                        { "name": "Strong Hit", "min_value": 10 }
                    ]
                }
            },
            "characterSchema": {
                "sections": [
                    {
                        "id": "basics",
                        "label": "Basic Information",
                        "fields": [
                            { "id": "name", "type": "text", "label": "Character Name", "required": true },
                            { "id": "playbook", "type": "text", "label": "Playbook" }
                        ]
                    },
                    {
                        "id": "stats",
                        "label": "Stats",
                        "fields": [
                            { "id": "cool", "type": "number", "label": "Cool", "min": -2, "max": 3 },
                            { "id": "hard", "type": "number", "label": "Hard", "min": -2, "max": 3 },
                            { "id": "hot", "type": "number", "label": "Hot", "min": -2, "max": 3 },
                            { "id": "sharp", "type": "number", "label": "Sharp", "min": -2, "max": 3 },
                            { "id": "weird", "type": "number", "label": "Weird", "min": -2, "max": 3 }
                        ]
                    },
                    {
                        "id": "harm",
                        "label": "Harm",
                        "fields": [
                            { "id": "harm_max", "type": "number", "label": "Max Harm" },
                            { "id": "harm_current", "type": "resource_pool", "label": "Current Harm", "max_field": "harm_max" }
                        ]
                    }
                ]
            },
            "actionEconomy": {
                "type": "freeform"
            },
            "aiGuidance": {
                "system_prompt_notes": "This is a Powered by the Apocalypse game. Players roll 2d6+stat. On 10+, strong hit. On 7-9, weak hit with complications. On 6-, miss and the MC makes a move.",
                "tone_guidance": "Fiction-first, player-driven narrative",
                "mechanical_notes": "2d6+stat. 10+ strong hit, 7-9 weak hit, 6- miss. Always follow the fiction.",
                "common_mistakes": ["Do not use target numbers - this system uses fixed threshold bands.", "Do not track initiative - PbtA is conversation-based."],
                "roll_format_example": "2d6+{stat}"
            }
        }
        """);

    /// <summary>
    /// Percentile template - roll d100 vs skill value (Call of Cthulhu, BRP).
    /// </summary>
    public static GameSystemTemplateDto Percentile() => new(
        Id: "percentile",
        Name: "Percentile (d100)",
        Description: "Roll d100 vs skill value (Call of Cthulhu, BRP)",
        Genre: "horror",
        DefinitionJson: """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "my-percentile-system",
                "name": "My Percentile System",
                "version": "1.0.0",
                "genre": "horror",
                "license": "user-created"
            },
            "diceConventions": {
                "primary": {
                    "type": "percentile",
                    "die": "d100",
                    "description": "Roll d100 under skill value to succeed"
                }
            },
            "resolutionRules": {
                "skillCheck": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": "<=",
                    "target_source": "skill_value"
                }
            },
            "characterSchema": {
                "sections": [
                    {
                        "id": "basics",
                        "label": "Basic Information",
                        "fields": [
                            { "id": "name", "type": "text", "label": "Investigator Name", "required": true },
                            { "id": "occupation", "type": "text", "label": "Occupation" },
                            { "id": "age", "type": "number", "label": "Age", "min": 15, "max": 90 }
                        ]
                    },
                    {
                        "id": "characteristics",
                        "label": "Characteristics",
                        "fields": [
                            { "id": "str", "type": "number", "label": "STR", "min": 1, "max": 99 },
                            { "id": "con", "type": "number", "label": "CON", "min": 1, "max": 99 },
                            { "id": "siz", "type": "number", "label": "SIZ", "min": 1, "max": 99 },
                            { "id": "dex", "type": "number", "label": "DEX", "min": 1, "max": 99 },
                            { "id": "app", "type": "number", "label": "APP", "min": 1, "max": 99 },
                            { "id": "int", "type": "number", "label": "INT", "min": 1, "max": 99 },
                            { "id": "pow", "type": "number", "label": "POW", "min": 1, "max": 99 },
                            { "id": "edu", "type": "number", "label": "EDU", "min": 1, "max": 99 }
                        ]
                    },
                    {
                        "id": "health",
                        "label": "Health & Sanity",
                        "fields": [
                            { "id": "hp_max", "type": "number", "label": "Max HP" },
                            { "id": "hp_current", "type": "resource_pool", "label": "Hit Points", "max_field": "hp_max" },
                            { "id": "san_max", "type": "number", "label": "Max Sanity" },
                            { "id": "san_current", "type": "resource_pool", "label": "Sanity", "max_field": "san_max" }
                        ]
                    }
                ]
            },
            "conditionSet": {
                "conditions": [
                    {
                        "name": "Temporary Insanity",
                        "description": "Investigator suffers a bout of temporary insanity.",
                        "effects": [
                            { "type": "roll_modifier", "scope": "all_checks", "effect": "penalty" }
                        ],
                        "duration_type": "rounds",
                        "stackable": false
                    }
                ]
            },
            "actionEconomy": {
                "type": "freeform"
            },
            "aiGuidance": {
                "system_prompt_notes": "This is a percentile-based horror RPG. Players roll d100 and must roll equal to or under their skill value to succeed. Lower rolls are better.",
                "tone_guidance": "Cosmic horror, investigation, dread",
                "mechanical_notes": "Roll d100 <= skill value. Critical success on 01. Fumble on 100. Hard success = half skill value. Extreme success = one-fifth skill value.",
                "common_mistakes": ["Do not use d20 mechanics - this is a d100 roll-under system.", "Do not forget sanity checks when encountering the unnatural."],
                "roll_format_example": "1d100 vs {skill_value}"
            }
        }
        """);

    /// <summary>
    /// Narrative/Freeform template - minimal mechanics, narrative-driven play.
    /// </summary>
    public static GameSystemTemplateDto Freeform() => new(
        Id: "freeform",
        Name: "Narrative / Freeform",
        Description: "Minimal mechanics, narrative-driven play",
        Genre: "various",
        DefinitionJson: """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "my-freeform-system",
                "name": "My Freeform System",
                "version": "1.0.0",
                "genre": "various",
                "license": "user-created",
                "description": "A minimal freeform system with no enforced mechanics."
            },
            "diceConventions": {},
            "resolutionRules": {},
            "characterSchema": {
                "sections": [
                    {
                        "id": "basics",
                        "label": "Character",
                        "fields": [
                            { "id": "name", "type": "text", "label": "Name", "required": true },
                            { "id": "concept", "type": "text", "label": "Concept" },
                            { "id": "description", "type": "text", "label": "Description" }
                        ]
                    }
                ]
            },
            "actionEconomy": {
                "type": "freeform"
            },
            "aiGuidance": {
                "system_prompt_notes": "This is a freeform narrative game with no enforced mechanical rules. Outcomes are determined through collaborative storytelling.",
                "tone_guidance": "Adapt to the campaign's genre and setting",
                "mechanical_notes": "No fixed dice conventions. The host may call for rolls when appropriate but there are no required mechanics."
            }
        }
        """);
}
