using Aircane.Application.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class GameSystemDefinitionSerializerTests
{
    private readonly GameSystemDefinitionSerializer _serializer = new();

    #region Valid JSON Parsing

    [Fact]
    public void ParseJson_ValidMinimalDefinition_ReturnsSuccess()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "test-system",
                "name": "Test System",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Definition);
        Assert.Equal("test-system", result.Definition!.Identifier);
        Assert.Equal("Test System", result.Definition.Name);
        Assert.Equal("1.0.0", result.Definition.Version);
        Assert.Equal(1, result.Definition.SchemaVersion);
        Assert.Equal("user-created", result.Definition.License);
    }

    [Fact]
    public void ParseJson_ValidFullDefinition_ParsesAllSections()
    {
        var json = GetFullValidJson();

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        var def = result.Definition!;

        // Metadata
        Assert.Equal("dnd-5e-2014", def.Identifier);
        Assert.Equal("Dungeons & Dragons 5th Edition (2014)", def.Name);
        Assert.Equal("1.0.0", def.Version);
        Assert.Equal("Wizards of the Coast", def.Publisher);
        Assert.Equal("fantasy", def.Genre);
        Assert.Equal("The 2014 core rules for D&D 5e.", def.Description);
        Assert.Equal("user-provided", def.License);
        Assert.Contains("d20", def.Tags);
        Assert.Contains("fantasy", def.Tags);

        // Dice conventions
        Assert.NotEmpty(def.DiceConventions);
        var primary = def.DiceConventions.First(c => c.Name == "primary");
        Assert.Equal(DiceConventionType.SingleDieModifier, primary.Type);
        Assert.Equal("d20", primary.Die);
        Assert.NotNull(primary.Advantage);
        Assert.Equal(2, primary.Advantage!.Roll);
        Assert.Equal("highest", primary.Advantage.Keep);

        // Resolution rules
        Assert.NotEmpty(def.ResolutionRules);
        var abilityCheck = def.ResolutionRules.First(r => r.Name == "abilityCheck");
        Assert.Equal(ResolutionRuleType.TargetNumber, abilityCheck.Type);
        Assert.Equal("primary", abilityCheck.Roll);
        Assert.Equal(">=", abilityCheck.Comparison);
        Assert.Equal("dc", abilityCheck.TargetSource);

        // Attack roll with critical conditions
        var attackRoll = def.ResolutionRules.First(r => r.Name == "attackRoll");
        Assert.NotNull(attackRoll.CriticalSuccess);
        Assert.Equal(20, attackRoll.CriticalSuccess!.NaturalRoll);
        Assert.NotNull(attackRoll.CriticalFailure);
        Assert.Equal(1, attackRoll.CriticalFailure!.NaturalRoll);

        // Character schema
        Assert.NotNull(def.CharacterSchema);
        Assert.NotEmpty(def.CharacterSchema!.Sections);

        // Condition set
        Assert.NotEmpty(def.ConditionSet);
        var poisoned = def.ConditionSet.First(c => c.Name == "Poisoned");
        Assert.NotEmpty(poisoned.Effects);
        Assert.Equal("until_save", poisoned.DurationType);
        Assert.False(poisoned.Stackable);

        // Action economy
        Assert.NotNull(def.ActionEconomy);
        Assert.Equal(ActionEconomyType.NamedSlots, def.ActionEconomy!.Type);
        Assert.NotNull(def.ActionEconomy.TurnStructure);
        Assert.NotEmpty(def.ActionEconomy.TurnStructure!.Slots);

        // Encounter budget
        Assert.NotNull(def.EncounterBudget);
        Assert.Equal(EncounterBudgetType.XpBudget, def.EncounterBudget!.Type);
        Assert.NotEmpty(def.EncounterBudget.DifficultyTiers);

        // AI guidance
        Assert.NotNull(def.AiGuidance);
        Assert.NotNull(def.AiGuidance!.SystemPromptNotes);
        Assert.NotEmpty(def.AiGuidance.CommonMistakes);
    }

    [Fact]
    public void ParseJson_SnakeCaseProperties_MapsCorrectly()
    {
        var json = """
        {
            "schema_version": 1,
            "metadata": {
                "id": "snake-test",
                "name": "Snake Case Test",
                "version": "1.0.0",
                "license": "community"
            },
            "dice_conventions": {
                "primary": {
                    "type": "dice_pool_success",
                    "die": "d6",
                    "success_threshold": 5,
                    "explode_threshold": 6
                }
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        var primary = result.Definition!.DiceConventions.First(c => c.Name == "primary");
        Assert.Equal(DiceConventionType.DicePoolSuccess, primary.Type);
        Assert.Equal(5, primary.SuccessThreshold);
        Assert.Equal(6, primary.ExplodeThreshold);
    }

    [Fact]
    public void ParseJson_CustomDiceConventions_ParsedCorrectly()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "custom-dice-test",
                "name": "Custom Dice Test",
                "version": "1.0.0",
                "license": "user-created"
            },
            "diceConventions": {
                "primary": {
                    "type": "single_die_modifier",
                    "die": "d20",
                    "modifier_sources": ["ability_modifier", "proficiency_bonus"]
                },
                "damage": {
                    "type": "expression",
                    "description": "Variable dice + modifier for damage rolls"
                },
                "custom": [
                    {
                        "name": "hit_dice",
                        "type": "expression",
                        "description": "Class-specific hit dice"
                    }
                ]
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Definition!.DiceConventions.Count);

        var primary = result.Definition.DiceConventions.First(c => c.Name == "primary");
        Assert.Equal(DiceConventionType.SingleDieModifier, primary.Type);
        Assert.Contains("ability_modifier", primary.ModifierSources);
        Assert.Contains("proficiency_bonus", primary.ModifierSources);

        var damage = result.Definition.DiceConventions.First(c => c.Name == "damage");
        Assert.Equal(DiceConventionType.Expression, damage.Type);

        var hitDice = result.Definition.DiceConventions.First(c => c.Name == "hit_dice");
        Assert.Equal(DiceConventionType.Expression, hitDice.Type);
    }

    [Fact]
    public void ParseJson_ResolutionRulesAsObject_KeyBecomesName()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "rules-test",
                "name": "Rules Test",
                "version": "1.0.0",
                "license": "user-created"
            },
            "resolutionRules": {
                "abilityCheck": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": ">=",
                    "target_source": "dc"
                },
                "savingThrow": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": ">=",
                    "target_source": "dc"
                }
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Definition!.ResolutionRules.Count);
        Assert.Contains(result.Definition.ResolutionRules, r => r.Name == "abilityCheck");
        Assert.Contains(result.Definition.ResolutionRules, r => r.Name == "savingThrow");
    }

    [Fact]
    public void ParseJson_StreamInput_WorksCorrectly()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "stream-test",
                "name": "Stream Test",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var result = _serializer.ParseJson(stream);

        Assert.True(result.IsSuccess);
        Assert.Equal("stream-test", result.Definition!.Identifier);
    }

    #endregion

    #region Syntax Error Reporting

    [Fact]
    public void ParseJson_InvalidJsonSyntax_ReportsErrorWithLineNumber()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "test"
                "name": "Missing comma"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(result.Errors[0].LineNumber);
        Assert.True(result.Errors[0].LineNumber > 0);
    }

    [Fact]
    public void ParseJson_EmptyInput_ReportsError()
    {
        var result = _serializer.ParseJson("");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("empty", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseJson_WhitespaceOnly_ReportsError()
    {
        var result = _serializer.ParseJson("   \n\t  ");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ParseJson_TrailingComma_Allowed()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "trailing-comma",
                "name": "Trailing Comma Test",
                "version": "1.0.0",
                "license": "user-created",
            },
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Unsupported Schema Version

    [Fact]
    public void ParseJson_UnsupportedSchemaVersion_RejectsWithClearError()
    {
        var json = """
        {
            "schemaVersion": 2,
            "metadata": {
                "id": "future-version",
                "name": "Future Version",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Unsupported schema version: 2", result.Errors[0].Message);
        Assert.Contains("Supported versions: 1", result.Errors[0].Message);
    }

    [Fact]
    public void ParseJson_SchemaVersionZero_RejectsWithClearError()
    {
        var json = """
        {
            "schemaVersion": 0,
            "metadata": {
                "id": "old-version",
                "name": "Old Version",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported schema version: 0", result.Errors[0].Message);
    }

    [Fact]
    public void ParseJson_SnakeCaseSchemaVersion_AlsoChecked()
    {
        var json = """
        {
            "schema_version": 99,
            "metadata": {
                "id": "snake-version",
                "name": "Snake Version",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported schema version: 99", result.Errors[0].Message);
    }

    #endregion

    #region Missing Required Fields

    [Fact]
    public void ParseJson_MissingMetadataId_ReportsError()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "name": "No ID",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.FieldPath == "metadata.id");
    }

    [Fact]
    public void ParseJson_MissingMetadataName_ReportsError()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "no-name",
                "version": "1.0.0",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.FieldPath == "metadata.name");
    }

    [Fact]
    public void ParseJson_MissingMetadataVersion_ReportsError()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "no-version",
                "name": "No Version",
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.FieldPath == "metadata.version");
    }

    [Fact]
    public void ParseJson_MissingAllRequiredFields_ReportsMultipleErrors()
    {
        var json = """
        {
            "schemaVersion": 1,
            "metadata": {
                "license": "user-created"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void ParseJson_MissingMetadataSection_ReportsErrors()
    {
        var json = """
        {
            "schemaVersion": 1
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    #endregion

    #region Enum Mapping

    [Theory]
    [InlineData("single_die_modifier", DiceConventionType.SingleDieModifier)]
    [InlineData("dice_pool_success", DiceConventionType.DicePoolSuccess)]
    [InlineData("fixed_dice_threshold", DiceConventionType.FixedDiceThreshold)]
    [InlineData("fudge", DiceConventionType.Fudge)]
    [InlineData("step_dice", DiceConventionType.StepDice)]
    [InlineData("percentile", DiceConventionType.Percentile)]
    [InlineData("expression", DiceConventionType.Expression)]
    public void ParseJson_DiceConventionTypes_MapCorrectly(string jsonValue, DiceConventionType expected)
    {
        var json = $$"""
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "enum-test",
                "name": "Enum Test",
                "version": "1.0.0",
                "license": "user-created"
            },
            "diceConventions": {
                "primary": {
                    "type": "{{jsonValue}}",
                    "die": "d6"
                }
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Definition!.DiceConventions[0].Type);
    }

    [Theory]
    [InlineData("target_number", ResolutionRuleType.TargetNumber)]
    [InlineData("opposed", ResolutionRuleType.Opposed)]
    [InlineData("degrees_of_success", ResolutionRuleType.DegreesOfSuccess)]
    [InlineData("margin", ResolutionRuleType.Margin)]
    [InlineData("threshold_bands", ResolutionRuleType.ThresholdBands)]
    public void ParseJson_ResolutionRuleTypes_MapCorrectly(string jsonValue, ResolutionRuleType expected)
    {
        var json = $$"""
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "enum-test",
                "name": "Enum Test",
                "version": "1.0.0",
                "license": "user-created"
            },
            "resolutionRules": {
                "testRule": {
                    "type": "{{jsonValue}}",
                    "roll": "primary"
                }
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Definition!.ResolutionRules[0].Type);
    }

    [Theory]
    [InlineData("named_slots", ActionEconomyType.NamedSlots)]
    [InlineData("action_points", ActionEconomyType.ActionPoints)]
    [InlineData("multi_action_penalty", ActionEconomyType.MultiActionPenalty)]
    [InlineData("freeform", ActionEconomyType.Freeform)]
    public void ParseJson_ActionEconomyTypes_MapCorrectly(string jsonValue, ActionEconomyType expected)
    {
        var json = $$"""
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "enum-test",
                "name": "Enum Test",
                "version": "1.0.0",
                "license": "user-created"
            },
            "actionEconomy": {
                "type": "{{jsonValue}}"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Definition!.ActionEconomy!.Type);
    }

    [Theory]
    [InlineData("xp_budget", EncounterBudgetType.XpBudget)]
    [InlineData("creature_level", EncounterBudgetType.CreatureLevel)]
    [InlineData("threat_rating", EncounterBudgetType.ThreatRating)]
    [InlineData("narrative_tiers", EncounterBudgetType.NarrativeTiers)]
    public void ParseJson_EncounterBudgetTypes_MapCorrectly(string jsonValue, EncounterBudgetType expected)
    {
        var json = $$"""
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "enum-test",
                "name": "Enum Test",
                "version": "1.0.0",
                "license": "user-created"
            },
            "encounterBudget": {
                "type": "{{jsonValue}}"
            }
        }
        """;

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Definition!.EncounterBudget!.Type);
    }

    #endregion

    #region Helper Methods

    private static string GetFullValidJson()
    {
        return """
        {
            "schemaVersion": 1,
            "metadata": {
                "id": "dnd-5e-2014",
                "name": "Dungeons & Dragons 5th Edition (2014)",
                "version": "1.0.0",
                "publisher": "Wizards of the Coast",
                "genre": "fantasy",
                "description": "The 2014 core rules for D&D 5e.",
                "license": "user-provided",
                "tags": ["d20", "fantasy", "levels"]
            },
            "diceConventions": {
                "primary": {
                    "type": "single_die_modifier",
                    "die": "d20",
                    "modifier_sources": ["ability_modifier", "proficiency_bonus"],
                    "advantage": { "roll": 2, "keep": "highest" },
                    "disadvantage": { "roll": 2, "keep": "lowest" }
                },
                "custom": [
                    {
                        "name": "hit_dice",
                        "type": "expression",
                        "description": "Class-specific hit dice for healing during rests"
                    }
                ]
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
                },
                "savingThrow": {
                    "type": "target_number",
                    "roll": "primary",
                    "comparison": ">=",
                    "target_source": "dc"
                }
            },
            "characterSchema": {
                "sections": [
                    {
                        "id": "basics",
                        "label": "Basic Information",
                        "fields": [
                            { "id": "name", "type": "text", "required": true, "label": "Character Name" },
                            { "id": "level", "type": "number", "required": true, "min": 1, "max": 20 }
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
                            { "type": "roll_modifier", "scope": "attack_rolls", "effect": "disadvantage" }
                        ],
                        "duration_type": "until_save",
                        "stackable": false
                    },
                    {
                        "name": "Prone",
                        "description": "Disadvantage on attack rolls.",
                        "effects": [
                            { "type": "roll_modifier", "scope": "attack_rolls", "effect": "disadvantage" }
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
                        { "name": "reaction", "count": 1, "label": "Reaction", "reset_on": "turn_start" }
                    ]
                }
            },
            "encounterBudget": {
                "type": "xp_budget",
                "difficulty_tiers": [
                    { "name": "Easy", "multiplier": 0.5 },
                    { "name": "Medium", "multiplier": 1.0 },
                    { "name": "Hard", "multiplier": 1.5 },
                    { "name": "Deadly", "multiplier": 2.0 }
                ],
                "formula": "sum(character_xp_threshold[level][difficulty]) * party_size_modifier",
                "creature_cost_field": "xp"
            },
            "aiGuidance": {
                "system_prompt_notes": "This is a d20-based fantasy RPG.",
                "tone_guidance": "High fantasy, heroic",
                "mechanical_notes": "Always ask for ability checks using d20+modifier vs DC.",
                "common_mistakes": [
                    "Do not use degrees of success."
                ],
                "roll_format_example": "1d20+{ability_modifier}+{proficiency_bonus}"
            }
        }
        """;
    }

    #endregion
}
