using Aircane.Application.Validation;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FluentValidation.TestHelper;
using Xunit;

namespace Aircane.UnitTests.Validation;

/// <summary>
/// Unit tests for <see cref="GameSystemDefinitionValidator"/>.
/// Validates required metadata, dice conventions, resolution rule cross-references,
/// character schema, conditions, action economy, and encounter budget.
/// </summary>
public class GameSystemDefinitionValidatorTests
{
    private readonly GameSystemDefinitionValidator _validator = new();

    private static GameSystemDefinition CreateValidDefinition() =>
        new(
            identifier: "test-system",
            name: "Test System",
            version: "1.0.0",
            schemaVersion: 1,
            license: "user-created")
        {
            DiceConventions =
            [
                new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier, Die = "d20" }
            ],
            ResolutionRules =
            [
                new ResolutionRule { Name = "abilityCheck", Type = ResolutionRuleType.TargetNumber, Roll = "primary" }
            ]
        };

    // ── Metadata validation ───────────────────────────────────────────────────

    [Fact]
    public void Valid_Definition_Passes()
    {
        var definition = CreateValidDefinition();
        var result = _validator.TestValidate(definition);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Identifier_Empty_Fails(string identifier)
    {
        var definition = CreateValidDefinition();
        definition.Identifier = identifier;
        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor(x => x.Identifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Empty_Fails(string name)
    {
        var definition = CreateValidDefinition();
        definition.Name = name;
        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Version_Empty_Fails(string version)
    {
        var definition = CreateValidDefinition();
        definition.Version = version;
        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void SchemaVersion_NotOne_Fails(int schemaVersion)
    {
        var definition = CreateValidDefinition();
        definition.SchemaVersion = schemaVersion;
        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor(x => x.SchemaVersion);
    }

    // ── Dice convention validation ────────────────────────────────────────────

    [Fact]
    public void DiceConvention_EmptyName_Fails()
    {
        var definition = CreateValidDefinition();
        definition.DiceConventions =
        [
            new DiceConvention { Name = "", Type = DiceConventionType.SingleDieModifier }
        ];
        definition.ResolutionRules = [];

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("DiceConventions[0].Name");
    }

    [Fact]
    public void DiceConvention_InvalidType_Fails()
    {
        var definition = CreateValidDefinition();
        definition.DiceConventions =
        [
            new DiceConvention { Name = "primary", Type = (DiceConventionType)999 }
        ];
        definition.ResolutionRules = [];

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("DiceConventions[0].Type");
    }

    // ── Resolution rule validation ────────────────────────────────────────────

    [Fact]
    public void ResolutionRule_EmptyName_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ResolutionRules =
        [
            new ResolutionRule { Name = "", Type = ResolutionRuleType.TargetNumber, Roll = "primary" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.Contains(result.Errors, e => e.PropertyName == "ResolutionRules[0].Name");
    }

    [Fact]
    public void ResolutionRule_EmptyRoll_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ResolutionRules =
        [
            new ResolutionRule { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.Contains(result.Errors, e => e.PropertyName == "ResolutionRules[0].Roll");
    }

    [Fact]
    public void ResolutionRule_RollReferencesNonExistentConvention_Fails()
    {
        var definition = CreateValidDefinition();
        definition.DiceConventions =
        [
            new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier }
        ];
        definition.ResolutionRules =
        [
            new ResolutionRule { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "nonexistent" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == "ResolutionRules[0].Roll" &&
            e.ErrorMessage.Contains("nonexistent"));
    }

    [Fact]
    public void ResolutionRule_RollReferencesExistingConvention_Passes()
    {
        var definition = CreateValidDefinition();
        definition.DiceConventions =
        [
            new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier }
        ];
        definition.ResolutionRules =
        [
            new ResolutionRule { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "primary" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.DoesNotContain(result.Errors, e => e.PropertyName.StartsWith("ResolutionRules"));
    }

    [Fact]
    public void ResolutionRule_InvalidType_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ResolutionRules =
        [
            new ResolutionRule { Name = "check", Type = (ResolutionRuleType)999, Roll = "primary" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.Contains(result.Errors, e => e.PropertyName == "ResolutionRules[0].Type");
    }

    // ── Character schema validation ───────────────────────────────────────────

    [Fact]
    public void CharacterSchema_SectionEmptyId_Fails()
    {
        var definition = CreateValidDefinition();
        definition.CharacterSchema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "",
                    Label = "Basics",
                    Fields = [new CharacterSchemaField { Id = "name", Type = CharacterFieldType.Text }]
                }
            ]
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("CharacterSchema.Sections[0].Id");
    }

    [Fact]
    public void CharacterSchema_SectionEmptyLabel_Fails()
    {
        var definition = CreateValidDefinition();
        definition.CharacterSchema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "basics",
                    Label = "",
                    Fields = [new CharacterSchemaField { Id = "name", Type = CharacterFieldType.Text }]
                }
            ]
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("CharacterSchema.Sections[0].Label");
    }

    [Fact]
    public void CharacterSchema_FieldEmptyId_Fails()
    {
        var definition = CreateValidDefinition();
        definition.CharacterSchema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "basics",
                    Label = "Basics",
                    Fields = [new CharacterSchemaField { Id = "", Type = CharacterFieldType.Text }]
                }
            ]
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("CharacterSchema.Sections[0].Fields[0].Id");
    }

    [Fact]
    public void CharacterSchema_FieldInvalidType_Fails()
    {
        var definition = CreateValidDefinition();
        definition.CharacterSchema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "basics",
                    Label = "Basics",
                    Fields = [new CharacterSchemaField { Id = "name", Type = (CharacterFieldType)999 }]
                }
            ]
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("CharacterSchema.Sections[0].Fields[0].Type");
    }

    [Fact]
    public void CharacterSchema_ValidSchema_Passes()
    {
        var definition = CreateValidDefinition();
        definition.CharacterSchema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "basics",
                    Label = "Basic Information",
                    Fields =
                    [
                        new CharacterSchemaField { Id = "name", Type = CharacterFieldType.Text },
                        new CharacterSchemaField { Id = "level", Type = CharacterFieldType.Number }
                    ]
                }
            ]
        };

        var result = _validator.TestValidate(definition);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Condition definition validation ───────────────────────────────────────

    [Fact]
    public void ConditionDefinition_EmptyName_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "", DurationType = "rounds" }
        ];

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("ConditionSet[0].Name");
    }

    [Fact]
    public void ConditionDefinition_EmptyDurationType_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "Poisoned", DurationType = "" }
        ];

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("ConditionSet[0].DurationType");
    }

    [Fact]
    public void ConditionDefinition_Valid_Passes()
    {
        var definition = CreateValidDefinition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "Poisoned", DurationType = "until_save" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.DoesNotContain(result.Errors, e => e.PropertyName.StartsWith("ConditionSet"));
    }

    // ── Action economy validation ─────────────────────────────────────────────

    [Fact]
    public void ActionEconomy_InvalidType_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = (ActionEconomyType)999
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("ActionEconomy.Type");
    }

    [Fact]
    public void ActionEconomy_NamedSlots_MissingTurnStructure_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = null
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("ActionEconomy.TurnStructure");
    }

    [Fact]
    public void ActionEconomy_NamedSlots_EmptySlots_Fails()
    {
        var definition = CreateValidDefinition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure { Slots = [] }
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("ActionEconomy.TurnStructure.Slots");
    }

    [Fact]
    public void ActionEconomy_NamedSlots_WithSlots_Passes()
    {
        var definition = CreateValidDefinition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
            }
        };

        var result = _validator.TestValidate(definition);
        Assert.DoesNotContain(result.Errors, e => e.PropertyName.StartsWith("ActionEconomy"));
    }

    [Fact]
    public void ActionEconomy_Freeform_NoTurnStructure_Passes()
    {
        var definition = CreateValidDefinition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.Freeform,
            TurnStructure = null
        };

        var result = _validator.TestValidate(definition);
        Assert.DoesNotContain(result.Errors, e => e.PropertyName.StartsWith("ActionEconomy"));
    }

    // ── Encounter budget validation ───────────────────────────────────────────

    [Fact]
    public void EncounterBudget_InvalidType_Fails()
    {
        var definition = CreateValidDefinition();
        definition.EncounterBudget = new EncounterBudgetFormula
        {
            Type = (EncounterBudgetType)999,
            DifficultyTiers = [new DifficultyTier { Name = "Easy", Multiplier = 0.5 }]
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("EncounterBudget.Type");
    }

    [Fact]
    public void EncounterBudget_EmptyDifficultyTiers_Fails()
    {
        var definition = CreateValidDefinition();
        definition.EncounterBudget = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers = []
        };

        var result = _validator.TestValidate(definition);
        result.ShouldHaveValidationErrorFor("EncounterBudget.DifficultyTiers");
    }

    [Fact]
    public void EncounterBudget_Valid_Passes()
    {
        var definition = CreateValidDefinition();
        definition.EncounterBudget = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 },
                new DifficultyTier { Name = "Hard", Multiplier = 1.5 }
            ]
        };

        var result = _validator.TestValidate(definition);
        Assert.DoesNotContain(result.Errors, e => e.PropertyName.StartsWith("EncounterBudget"));
    }

    // ── Null optional sections pass ───────────────────────────────────────────

    [Fact]
    public void NullOptionalSections_Passes()
    {
        var definition = CreateValidDefinition();
        definition.CharacterSchema = null;
        definition.ActionEconomy = null;
        definition.EncounterBudget = null;
        definition.AiGuidance = null;
        definition.ConditionSet = [];

        var result = _validator.TestValidate(definition);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Field-path error format ───────────────────────────────────────────────

    [Fact]
    public void Errors_UseFieldPathFormat()
    {
        var definition = CreateValidDefinition();
        definition.DiceConventions =
        [
            new DiceConvention { Name = "valid", Type = DiceConventionType.SingleDieModifier },
            new DiceConvention { Name = "", Type = DiceConventionType.DicePoolSuccess }
        ];
        definition.ResolutionRules =
        [
            new ResolutionRule { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "valid" }
        ];

        var result = _validator.TestValidate(definition);
        Assert.Contains(result.Errors, e => e.PropertyName == "DiceConventions[1].Name");
    }
}
