using System.Text.Json;
using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class GameSystemDefinitionPrinterTests
{
    private readonly GameSystemDefinitionSerializer _serializer = new();

    #region Basic PrintJson Output

    [Fact]
    public void PrintJson_MinimalDefinition_ProducesValidJson()
    {
        var definition = CreateMinimalDefinition();

        var json = _serializer.PrintJson(definition);

        // Should be valid JSON
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void PrintJson_MinimalDefinition_ContainsSchemaVersion()
    {
        var definition = CreateMinimalDefinition();

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("schemaVersion", out var sv));
        Assert.Equal(1, sv.GetInt32());
    }

    [Fact]
    public void PrintJson_MinimalDefinition_ContainsMetadata()
    {
        var definition = CreateMinimalDefinition();

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("metadata", out var metadata));
        Assert.Equal("test-system", metadata.GetProperty("id").GetString());
        Assert.Equal("Test System", metadata.GetProperty("name").GetString());
        Assert.Equal("1.0.0", metadata.GetProperty("version").GetString());
        Assert.Equal("user-created", metadata.GetProperty("license").GetString());
    }

    [Fact]
    public void PrintJson_WithOptionalMetadata_IncludesAllFields()
    {
        var definition = new GameSystemDefinition(
            identifier: "full-meta",
            name: "Full Metadata",
            version: "2.0.0",
            schemaVersion: 1,
            license: "community",
            publisher: "Test Publisher",
            genre: "sci-fi",
            description: "A test description")
        {
            Tags = new List<string> { "space", "lasers" }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.Equal("Test Publisher", metadata.GetProperty("publisher").GetString());
        Assert.Equal("sci-fi", metadata.GetProperty("genre").GetString());
        Assert.Equal("A test description", metadata.GetProperty("description").GetString());
        Assert.Equal(2, metadata.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public void PrintJson_NullOptionalSections_OmitsFromOutput()
    {
        var definition = CreateMinimalDefinition();

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("characterSchema", out _));
        Assert.False(root.TryGetProperty("actionEconomy", out _));
        Assert.False(root.TryGetProperty("encounterBudget", out _));
        Assert.False(root.TryGetProperty("aiGuidance", out _));
    }

    [Fact]
    public void PrintJson_EmptyCollections_OmitsFromOutput()
    {
        var definition = CreateMinimalDefinition();
        // DiceConventions and ResolutionRules are empty by default

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("diceConventions", out _));
        Assert.False(root.TryGetProperty("resolutionRules", out _));
        Assert.False(root.TryGetProperty("conditionSet", out _));
    }

    #endregion

    #region Dice Conventions Serialization

    [Fact]
    public void PrintJson_PrimaryDiceConvention_SerializedUnderPrimaryKey()
    {
        var definition = CreateMinimalDefinition();
        definition.DiceConventions = new List<DiceConvention>
        {
            new()
            {
                Name = "primary",
                Type = DiceConventionType.SingleDieModifier,
                Die = "d20",
                ModifierSources = new List<string> { "ability_modifier", "proficiency_bonus" },
                Advantage = new KeepDirective { Roll = 2, Keep = "highest" },
                Disadvantage = new KeepDirective { Roll = 2, Keep = "lowest" }
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var diceConventions = doc.RootElement.GetProperty("diceConventions");
        var primary = diceConventions.GetProperty("primary");
        Assert.Equal("single_die_modifier", primary.GetProperty("type").GetString());
        Assert.Equal("d20", primary.GetProperty("die").GetString());
        Assert.Equal(2, primary.GetProperty("modifier_sources").GetArrayLength());
        Assert.Equal(2, primary.GetProperty("advantage").GetProperty("roll").GetInt32());
        Assert.Equal("highest", primary.GetProperty("advantage").GetProperty("keep").GetString());
    }

    [Fact]
    public void PrintJson_NamedDiceConvention_SerializedAsTopLevelKey()
    {
        var definition = CreateMinimalDefinition();
        definition.DiceConventions = new List<DiceConvention>
        {
            new() { Name = "primary", Type = DiceConventionType.SingleDieModifier, Die = "d20" },
            new() { Name = "damage", Type = DiceConventionType.Expression, Description = "Damage rolls" }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var diceConventions = doc.RootElement.GetProperty("diceConventions");
        Assert.True(diceConventions.TryGetProperty("damage", out var damage));
        Assert.Equal("expression", damage.GetProperty("type").GetString());
        Assert.Equal("Damage rolls", damage.GetProperty("description").GetString());
    }

    [Fact]
    public void PrintJson_CustomDiceConventions_SerializedInCustomArray()
    {
        var definition = CreateMinimalDefinition();
        definition.DiceConventions = new List<DiceConvention>
        {
            new() { Name = "primary", Type = DiceConventionType.SingleDieModifier, Die = "d20" },
            new() { Name = "hit_dice", Type = DiceConventionType.Expression, Description = "Hit dice" }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var diceConventions = doc.RootElement.GetProperty("diceConventions");
        Assert.True(diceConventions.TryGetProperty("custom", out var custom));
        Assert.Equal(JsonValueKind.Array, custom.ValueKind);
        Assert.Equal(1, custom.GetArrayLength());
        Assert.Equal("hit_dice", custom[0].GetProperty("name").GetString());
    }

    [Fact]
    public void PrintJson_DicePoolConvention_IncludesThresholds()
    {
        var definition = CreateMinimalDefinition();
        definition.DiceConventions = new List<DiceConvention>
        {
            new()
            {
                Name = "primary",
                Type = DiceConventionType.DicePoolSuccess,
                Die = "d6",
                SuccessThreshold = 5,
                ExplodeThreshold = 6
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var primary = doc.RootElement.GetProperty("diceConventions").GetProperty("primary");
        Assert.Equal(5, primary.GetProperty("success_threshold").GetInt32());
        Assert.Equal(6, primary.GetProperty("explode_threshold").GetInt32());
    }

    #endregion

    #region Resolution Rules Serialization

    [Fact]
    public void PrintJson_ResolutionRules_SerializedAsObjectWithNameKeys()
    {
        var definition = CreateMinimalDefinition();
        definition.ResolutionRules = new List<ResolutionRule>
        {
            new()
            {
                Name = "abilityCheck",
                Type = ResolutionRuleType.TargetNumber,
                Roll = "primary",
                Comparison = ">=",
                TargetSource = "dc"
            },
            new()
            {
                Name = "attackRoll",
                Type = ResolutionRuleType.TargetNumber,
                Roll = "primary",
                Comparison = ">=",
                TargetSource = "ac",
                CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
                CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var rules = doc.RootElement.GetProperty("resolutionRules");
        Assert.True(rules.TryGetProperty("abilityCheck", out var abilityCheck));
        Assert.Equal("target_number", abilityCheck.GetProperty("type").GetString());
        Assert.Equal(">=", abilityCheck.GetProperty("comparison").GetString());
        Assert.Equal("dc", abilityCheck.GetProperty("target_source").GetString());

        Assert.True(rules.TryGetProperty("attackRoll", out var attackRoll));
        Assert.Equal(20, attackRoll.GetProperty("critical_success").GetProperty("natural_roll").GetInt32());
        Assert.Equal(1, attackRoll.GetProperty("critical_failure").GetProperty("natural_roll").GetInt32());
    }

    [Fact]
    public void PrintJson_ResolutionRuleWithDegrees_IncludesDegreesArray()
    {
        var definition = CreateMinimalDefinition();
        definition.ResolutionRules = new List<ResolutionRule>
        {
            new()
            {
                Name = "skillCheck",
                Type = ResolutionRuleType.DegreesOfSuccess,
                Roll = "primary",
                DegreesOfSuccess = new List<DegreeThreshold>
                {
                    new() { Name = "Critical Success", MinValue = 30 },
                    new() { Name = "Success", MinValue = 20, MaxValue = 29 },
                    new() { Name = "Failure", MinValue = 10, MaxValue = 19 },
                    new() { Name = "Critical Failure", MaxValue = 9 }
                }
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var rule = doc.RootElement.GetProperty("resolutionRules").GetProperty("skillCheck");
        var degrees = rule.GetProperty("degrees_of_success");
        Assert.Equal(4, degrees.GetArrayLength());
        Assert.Equal("Critical Success", degrees[0].GetProperty("name").GetString());
        Assert.Equal(30, degrees[0].GetProperty("min_value").GetInt32());
    }

    #endregion

    #region Condition Set Serialization

    [Fact]
    public void PrintJson_ConditionSet_WrappedInConditionsArray()
    {
        var definition = CreateMinimalDefinition();
        definition.ConditionSet = new List<ConditionDefinition>
        {
            new()
            {
                Name = "Poisoned",
                Description = "Disadvantage on attack rolls.",
                Effects = new List<ConditionEffect>
                {
                    new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" }
                },
                DurationType = "until_save",
                Stackable = false
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var conditionSet = doc.RootElement.GetProperty("conditionSet");
        var conditions = conditionSet.GetProperty("conditions");
        Assert.Equal(1, conditions.GetArrayLength());
        Assert.Equal("Poisoned", conditions[0].GetProperty("name").GetString());
        Assert.Equal("until_save", conditions[0].GetProperty("duration_type").GetString());
        Assert.False(conditions[0].GetProperty("stackable").GetBoolean());
    }

    #endregion

    #region Action Economy Serialization

    [Fact]
    public void PrintJson_ActionEconomy_SerializesCorrectly()
    {
        var definition = CreateMinimalDefinition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots = new List<ActionSlot>
                {
                    new() { Name = "action", Count = 1, Label = "Action" },
                    new() { Name = "bonus_action", Count = 1, Label = "Bonus Action" },
                    new() { Name = "reaction", Count = 1, Label = "Reaction", ResetOn = "turn_start" }
                }
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var economy = doc.RootElement.GetProperty("actionEconomy");
        Assert.Equal("named_slots", economy.GetProperty("type").GetString());
        var slots = economy.GetProperty("turn_structure").GetProperty("slots");
        Assert.Equal(3, slots.GetArrayLength());
        Assert.Equal("action", slots[0].GetProperty("name").GetString());
        Assert.Equal("turn_start", slots[2].GetProperty("reset_on").GetString());
    }

    #endregion

    #region Encounter Budget Serialization

    [Fact]
    public void PrintJson_EncounterBudget_SerializesCorrectly()
    {
        var definition = CreateMinimalDefinition();
        definition.EncounterBudget = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers = new List<DifficultyTier>
            {
                new() { Name = "Easy", Multiplier = 0.5 },
                new() { Name = "Hard", Multiplier = 1.5 }
            },
            Formula = "sum(xp_threshold) * modifier",
            CreatureCostField = "xp"
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var budget = doc.RootElement.GetProperty("encounterBudget");
        Assert.Equal("xp_budget", budget.GetProperty("type").GetString());
        Assert.Equal(2, budget.GetProperty("difficulty_tiers").GetArrayLength());
        Assert.Equal("sum(xp_threshold) * modifier", budget.GetProperty("formula").GetString());
        Assert.Equal("xp", budget.GetProperty("creature_cost_field").GetString());
    }

    #endregion

    #region AI Guidance Serialization

    [Fact]
    public void PrintJson_AiGuidance_SerializesCorrectly()
    {
        var definition = CreateMinimalDefinition();
        definition.AiGuidance = new AiGuidance
        {
            SystemPromptNotes = "This is a d20 system.",
            ToneGuidance = "High fantasy",
            MechanicalNotes = "Use d20+modifier vs DC.",
            CommonMistakes = new List<string> { "Don't use degrees of success." },
            RollFormatExample = "1d20+{mod}"
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var guidance = doc.RootElement.GetProperty("aiGuidance");
        Assert.Equal("This is a d20 system.", guidance.GetProperty("system_prompt_notes").GetString());
        Assert.Equal("High fantasy", guidance.GetProperty("tone_guidance").GetString());
        Assert.Equal("Use d20+modifier vs DC.", guidance.GetProperty("mechanical_notes").GetString());
        Assert.Equal(1, guidance.GetProperty("common_mistakes").GetArrayLength());
        Assert.Equal("1d20+{mod}", guidance.GetProperty("roll_format_example").GetString());
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void PrintJson_ThenParseJson_ProducesEquivalentDefinition()
    {
        var original = CreateFullDefinition();

        var json = _serializer.PrintJson(original);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, $"Parse failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        var roundTripped = result.Definition!;

        // Metadata
        Assert.Equal(original.Identifier, roundTripped.Identifier);
        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.Version, roundTripped.Version);
        Assert.Equal(original.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(original.Publisher, roundTripped.Publisher);
        Assert.Equal(original.Genre, roundTripped.Genre);
        Assert.Equal(original.Description, roundTripped.Description);
        Assert.Equal(original.License, roundTripped.License);
        Assert.Equal(original.Tags, roundTripped.Tags);

        // Dice conventions
        Assert.Equal(original.DiceConventions.Count, roundTripped.DiceConventions.Count);
        var origPrimary = original.DiceConventions.First(c => c.Name == "primary");
        var rtPrimary = roundTripped.DiceConventions.First(c => c.Name == "primary");
        Assert.Equal(origPrimary.Type, rtPrimary.Type);
        Assert.Equal(origPrimary.Die, rtPrimary.Die);
        Assert.Equal(origPrimary.Advantage?.Roll, rtPrimary.Advantage?.Roll);
        Assert.Equal(origPrimary.Advantage?.Keep, rtPrimary.Advantage?.Keep);

        // Resolution rules
        Assert.Equal(original.ResolutionRules.Count, roundTripped.ResolutionRules.Count);
        foreach (var origRule in original.ResolutionRules)
        {
            var rtRule = roundTripped.ResolutionRules.First(r => r.Name == origRule.Name);
            Assert.Equal(origRule.Type, rtRule.Type);
            Assert.Equal(origRule.Roll, rtRule.Roll);
            Assert.Equal(origRule.Comparison, rtRule.Comparison);
            Assert.Equal(origRule.TargetSource, rtRule.TargetSource);
        }

        // Condition set
        Assert.Equal(original.ConditionSet.Count, roundTripped.ConditionSet.Count);

        // Action economy
        Assert.NotNull(roundTripped.ActionEconomy);
        Assert.Equal(original.ActionEconomy!.Type, roundTripped.ActionEconomy!.Type);

        // Encounter budget
        Assert.NotNull(roundTripped.EncounterBudget);
        Assert.Equal(original.EncounterBudget!.Type, roundTripped.EncounterBudget!.Type);

        // AI guidance
        Assert.NotNull(roundTripped.AiGuidance);
        Assert.Equal(original.AiGuidance!.SystemPromptNotes, roundTripped.AiGuidance!.SystemPromptNotes);
    }

    [Fact]
    public void PrintJson_OutputCanBeReparsed_WithoutErrors()
    {
        var definition = CreateFullDefinition();

        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void PrintJson_NullDefinition_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _serializer.PrintJson(null!));
    }

    [Fact]
    public void PrintJson_CharacterSchemaWithVisibility_SerializesCorrectly()
    {
        var definition = CreateMinimalDefinition();
        definition.CharacterSchema = new CharacterSchema
        {
            Sections = new List<CharacterSchemaSection>
            {
                new()
                {
                    Id = "spells",
                    Label = "Spellcasting",
                    VisibleWhen = new VisibilityCondition
                    {
                        Field = "class",
                        In = new List<string> { "Wizard", "Cleric" }
                    },
                    Fields = new List<CharacterSchemaField>
                    {
                        new()
                        {
                            Id = "spell_slots",
                            Type = CharacterFieldType.ResourcePool,
                            Label = "Spell Slots",
                            MaxField = "spell_slots_max"
                        }
                    }
                }
            }
        };

        var json = _serializer.PrintJson(definition);

        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("characterSchema")
            .GetProperty("sections")[0];
        Assert.Equal("spells", section.GetProperty("id").GetString());
        var visibleWhen = section.GetProperty("visible_when");
        Assert.Equal("class", visibleWhen.GetProperty("field").GetString());
        Assert.Equal(2, visibleWhen.GetProperty("in").GetArrayLength());
    }

    #endregion

    #region Helper Methods

    private static GameSystemDefinition CreateMinimalDefinition()
    {
        return new GameSystemDefinition(
            identifier: "test-system",
            name: "Test System",
            version: "1.0.0",
            schemaVersion: 1,
            license: "user-created");
    }

    private static GameSystemDefinition CreateFullDefinition()
    {
        var definition = new GameSystemDefinition(
            identifier: "dnd-5e-2014",
            name: "Dungeons & Dragons 5th Edition (2014)",
            version: "1.0.0",
            schemaVersion: 1,
            license: "user-provided",
            publisher: "Wizards of the Coast",
            genre: "fantasy",
            description: "The 2014 core rules for D&D 5e.")
        {
            Tags = new List<string> { "d20", "fantasy", "levels" },
            DiceConventions = new List<DiceConvention>
            {
                new()
                {
                    Name = "primary",
                    Type = DiceConventionType.SingleDieModifier,
                    Die = "d20",
                    ModifierSources = new List<string> { "ability_modifier", "proficiency_bonus" },
                    Advantage = new KeepDirective { Roll = 2, Keep = "highest" },
                    Disadvantage = new KeepDirective { Roll = 2, Keep = "lowest" }
                },
                new()
                {
                    Name = "hit_dice",
                    Type = DiceConventionType.Expression,
                    Description = "Class-specific hit dice for healing during rests"
                }
            },
            ResolutionRules = new List<ResolutionRule>
            {
                new()
                {
                    Name = "abilityCheck",
                    Type = ResolutionRuleType.TargetNumber,
                    Roll = "primary",
                    Comparison = ">=",
                    TargetSource = "dc"
                },
                new()
                {
                    Name = "attackRoll",
                    Type = ResolutionRuleType.TargetNumber,
                    Roll = "primary",
                    Comparison = ">=",
                    TargetSource = "ac",
                    CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
                    CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
                }
            },
            CharacterSchema = new CharacterSchema
            {
                Sections = new List<CharacterSchemaSection>
                {
                    new()
                    {
                        Id = "basics",
                        Label = "Basic Information",
                        Fields = new List<CharacterSchemaField>
                        {
                            new() { Id = "name", Type = CharacterFieldType.Text, Required = true, Label = "Character Name" },
                            new() { Id = "level", Type = CharacterFieldType.Number, Required = true, Min = 1, Max = 20 }
                        }
                    }
                }
            },
            ConditionSet = new List<ConditionDefinition>
            {
                new()
                {
                    Name = "Poisoned",
                    Description = "Disadvantage on attack rolls and ability checks.",
                    Effects = new List<ConditionEffect>
                    {
                        new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" }
                    },
                    DurationType = "until_save",
                    Stackable = false
                }
            },
            ActionEconomy = new ActionEconomyDefinition
            {
                Type = ActionEconomyType.NamedSlots,
                TurnStructure = new TurnStructure
                {
                    Slots = new List<ActionSlot>
                    {
                        new() { Name = "action", Count = 1, Label = "Action" },
                        new() { Name = "bonus_action", Count = 1, Label = "Bonus Action" },
                        new() { Name = "reaction", Count = 1, Label = "Reaction", ResetOn = "turn_start" }
                    }
                }
            },
            EncounterBudget = new EncounterBudgetFormula
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
            },
            AiGuidance = new AiGuidance
            {
                SystemPromptNotes = "This is a d20-based fantasy RPG.",
                ToneGuidance = "High fantasy, heroic",
                MechanicalNotes = "Always ask for ability checks using d20+modifier vs DC.",
                CommonMistakes = new List<string> { "Do not use degrees of success." },
                RollFormatExample = "1d20+{ability_modifier}+{proficiency_bonus}"
            }
        };

        return definition;
    }

    #endregion
}
