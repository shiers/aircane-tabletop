using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class CharacterSchemaEngineTests
{
    private readonly CharacterSchemaEngine _engine = new();

    private static CharacterSchema CreateTestSchema() => new()
    {
        Sections =
        [
            new CharacterSchemaSection
            {
                Id = "basics",
                Label = "Basic Information",
                Fields =
                [
                    new CharacterSchemaField { Id = "name", Type = CharacterFieldType.Text, Label = "Character Name", Required = true },
                    new CharacterSchemaField { Id = "level", Type = CharacterFieldType.Number, Label = "Level", Required = true, Min = 1, Max = 20 },
                    new CharacterSchemaField { Id = "class", Type = CharacterFieldType.Enum, Label = "Class", Options = ["Fighter", "Wizard", "Rogue"] },
                    new CharacterSchemaField { Id = "is_npc", Type = CharacterFieldType.Boolean, Label = "Is NPC" }
                ]
            },
            new CharacterSchemaSection
            {
                Id = "abilities",
                Label = "Ability Scores",
                Fields =
                [
                    new CharacterSchemaField { Id = "str", Type = CharacterFieldType.Number, Label = "Strength", Min = 1, Max = 30 },
                    new CharacterSchemaField { Id = "str_mod", Type = CharacterFieldType.Calculated, Label = "STR Mod", Formula = "floor((str - 10) / 2)" }
                ]
            },
            new CharacterSchemaSection
            {
                Id = "combat",
                Label = "Combat",
                Fields =
                [
                    new CharacterSchemaField { Id = "hp_current", Type = CharacterFieldType.ResourcePool, Label = "Hit Points", MaxField = "hp_max" },
                    new CharacterSchemaField { Id = "hp_max", Type = CharacterFieldType.Number, Label = "Max HP" },
                    new CharacterSchemaField { Id = "hit_dice", Type = CharacterFieldType.DiceExpression, Label = "Hit Dice" },
                    new CharacterSchemaField { Id = "languages", Type = CharacterFieldType.List, Label = "Languages" },
                    new CharacterSchemaField
                    {
                        Id = "inventory",
                        Type = CharacterFieldType.Repeating,
                        Label = "Inventory",
                        ItemSchema = new Dictionary<string, string> { ["name"] = "text", ["weight"] = "number" }
                    }
                ]
            },
            new CharacterSchemaSection
            {
                Id = "spells",
                Label = "Spellcasting",
                VisibleWhen = new VisibilityCondition { Field = "class", In = ["Wizard"] },
                Fields =
                [
                    new CharacterSchemaField { Id = "spell_slots", Type = CharacterFieldType.Number, Label = "Spell Slots" }
                ]
            }
        ]
    };

    #region Validate Tests

    [Fact]
    public void Validate_ValidCharacter_ReturnsValid()
    {
        var schema = CreateTestSchema();
        var json = """
        {
            "name": "Gandalf",
            "level": 10,
            "class": "Wizard",
            "is_npc": false,
            "str": 14,
            "hp_current": 45,
            "hp_max": 50,
            "hit_dice": "1d10",
            "languages": ["Common", "Elvish"],
            "inventory": [{"name": "Staff", "weight": 4}]
        }
        """;

        var result = _engine.Validate(json, schema);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingRequiredField_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "level": 5 }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "name" && e.Message.Contains("required"));
    }

    [Fact]
    public void Validate_NumberBelowMin_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 0 }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "level" && e.Message.Contains("at least 1"));
    }

    [Fact]
    public void Validate_NumberAboveMax_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 25 }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "level" && e.Message.Contains("at most 20"));
    }

    [Fact]
    public void Validate_InvalidEnumValue_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "class": "Bard" }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "class" && e.Message.Contains("must be one of"));
    }

    [Fact]
    public void Validate_WrongType_Text_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": 123, "level": 5 }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "name" && e.Message.Contains("string"));
    }

    [Fact]
    public void Validate_WrongType_Number_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": "five" }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "level" && e.Message.Contains("number"));
    }

    [Fact]
    public void Validate_WrongType_Boolean_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "is_npc": "yes" }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "is_npc" && e.Message.Contains("boolean"));
    }

    [Fact]
    public void Validate_WrongType_List_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "languages": "Common" }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "languages" && e.Message.Contains("array"));
    }

    [Fact]
    public void Validate_WrongType_Repeating_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "inventory": "sword" }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "inventory" && e.Message.Contains("array"));
    }

    [Fact]
    public void Validate_ResourcePool_AsNumber_IsValid()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "hp_current": 30 }""";

        var result = _engine.Validate(json, schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ResourcePool_AsObject_IsValid()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "hp_current": {"current": 30, "max": 50} }""";

        var result = _engine.Validate(json, schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ResourcePool_InvalidType_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "hp_current": "full" }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "hp_current");
    }

    [Fact]
    public void Validate_EmptyJson_ReturnsError()
    {
        var schema = CreateTestSchema();

        var result = _engine.Validate("", schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("empty"));
    }

    [Fact]
    public void Validate_InvalidJson_ReturnsError()
    {
        var schema = CreateTestSchema();

        var result = _engine.Validate("{invalid json", schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid JSON"));
    }

    [Fact]
    public void Validate_OptionalFieldMissing_IsValid()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5 }""";

        var result = _engine.Validate(json, schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DiceExpression_WrongType_ReturnsError()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "hit_dice": 10 }""";

        var result = _engine.Validate(json, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldPath == "hit_dice" && e.Message.Contains("string"));
    }

    [Fact]
    public void Validate_EnumCaseInsensitive_IsValid()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test", "level": 5, "class": "fighter" }""";

        var result = _engine.Validate(json, schema);

        Assert.True(result.IsValid);
    }

    #endregion

    #region ComputeCalculatedFields Tests

    [Fact]
    public void ComputeCalculatedFields_SimpleFormula_ReturnsCorrectValue()
    {
        var schema = CreateTestSchema();
        var json = """{ "str": 14 }""";

        var result = _engine.ComputeCalculatedFields(json, schema);

        Assert.True(result.ContainsKey("str_mod"));
        Assert.Equal(2.0, result["str_mod"]);
    }

    [Fact]
    public void ComputeCalculatedFields_FloorFunction_WorksCorrectly()
    {
        var schema = CreateTestSchema();
        var json = """{ "str": 15 }""";

        var result = _engine.ComputeCalculatedFields(json, schema);

        // floor((15 - 10) / 2) = floor(2.5) = 2
        Assert.Equal(2.0, result["str_mod"]);
    }

    [Fact]
    public void ComputeCalculatedFields_NegativeModifier_WorksCorrectly()
    {
        var schema = CreateTestSchema();
        var json = """{ "str": 8 }""";

        var result = _engine.ComputeCalculatedFields(json, schema);

        // floor((8 - 10) / 2) = floor(-1) = -1
        Assert.Equal(-1.0, result["str_mod"]);
    }

    [Fact]
    public void ComputeCalculatedFields_MissingSourceField_ReturnsNull()
    {
        var schema = CreateTestSchema();
        var json = """{ "name": "Test" }""";

        var result = _engine.ComputeCalculatedFields(json, schema);

        Assert.True(result.ContainsKey("str_mod"));
        Assert.Null(result["str_mod"]);
    }

    [Fact]
    public void ComputeCalculatedFields_EmptyJson_ReturnsEmptyDictionary()
    {
        var schema = CreateTestSchema();

        var result = _engine.ComputeCalculatedFields("", schema);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeCalculatedFields_InvalidJson_ReturnsEmptyDictionary()
    {
        var schema = CreateTestSchema();

        var result = _engine.ComputeCalculatedFields("{invalid", schema);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeCalculatedFields_MultipleCalculatedFields_AllComputed()
    {
        var schema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "abilities",
                    Label = "Abilities",
                    Fields =
                    [
                        new CharacterSchemaField { Id = "str", Type = CharacterFieldType.Number },
                        new CharacterSchemaField { Id = "dex", Type = CharacterFieldType.Number },
                        new CharacterSchemaField { Id = "str_mod", Type = CharacterFieldType.Calculated, Formula = "floor((str - 10) / 2)" },
                        new CharacterSchemaField { Id = "dex_mod", Type = CharacterFieldType.Calculated, Formula = "floor((dex - 10) / 2)" }
                    ]
                }
            ]
        };
        var json = """{ "str": 16, "dex": 12 }""";

        var result = _engine.ComputeCalculatedFields(json, schema);

        Assert.Equal(3.0, result["str_mod"]); // floor((16-10)/2) = 3
        Assert.Equal(1.0, result["dex_mod"]); // floor((12-10)/2) = 1
    }

    #endregion

    #region GenerateFormDescriptor Tests

    [Fact]
    public void GenerateFormDescriptor_ContainsAllSections()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        Assert.Equal(4, descriptor.Sections.Count);
        Assert.Equal("basics", descriptor.Sections[0].Id);
        Assert.Equal("abilities", descriptor.Sections[1].Id);
        Assert.Equal("combat", descriptor.Sections[2].Id);
        Assert.Equal("spells", descriptor.Sections[3].Id);
    }

    [Fact]
    public void GenerateFormDescriptor_ContainsAllFields()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var basicsSection = descriptor.Sections[0];
        Assert.Equal(4, basicsSection.Fields.Count);
        Assert.Equal("name", basicsSection.Fields[0].Id);
        Assert.Equal("level", basicsSection.Fields[1].Id);
        Assert.Equal("class", basicsSection.Fields[2].Id);
        Assert.Equal("is_npc", basicsSection.Fields[3].Id);
    }

    [Fact]
    public void GenerateFormDescriptor_FieldTypes_AreCorrect()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var basicsSection = descriptor.Sections[0];
        Assert.Equal(CharacterFieldType.Text, basicsSection.Fields[0].Type);
        Assert.Equal(CharacterFieldType.Number, basicsSection.Fields[1].Type);
        Assert.Equal(CharacterFieldType.Enum, basicsSection.Fields[2].Type);
        Assert.Equal(CharacterFieldType.Boolean, basicsSection.Fields[3].Type);
    }

    [Fact]
    public void GenerateFormDescriptor_Labels_AreCorrect()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        Assert.Equal("Character Name", descriptor.Sections[0].Fields[0].Label);
        Assert.Equal("Level", descriptor.Sections[0].Fields[1].Label);
    }

    [Fact]
    public void GenerateFormDescriptor_ValidationRules_AreIncluded()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var levelField = descriptor.Sections[0].Fields[1];
        Assert.True(levelField.Required);
        Assert.Equal(1, levelField.Min);
        Assert.Equal(20, levelField.Max);
    }

    [Fact]
    public void GenerateFormDescriptor_EnumOptions_AreIncluded()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var classField = descriptor.Sections[0].Fields[2];
        Assert.NotNull(classField.Options);
        Assert.Equal(3, classField.Options!.Count);
        Assert.Contains("Fighter", classField.Options);
        Assert.Contains("Wizard", classField.Options);
        Assert.Contains("Rogue", classField.Options);
    }

    [Fact]
    public void GenerateFormDescriptor_CalculatedField_IsReadOnly()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var strModField = descriptor.Sections[1].Fields[1];
        Assert.True(strModField.IsReadOnly);
        Assert.Equal("floor((str - 10) / 2)", strModField.Formula);
    }

    [Fact]
    public void GenerateFormDescriptor_VisibleWhen_IsIncluded()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var spellsSection = descriptor.Sections[3];
        Assert.NotNull(spellsSection.VisibleWhen);
        Assert.Equal("class", spellsSection.VisibleWhen!.Field);
        Assert.Contains("Wizard", spellsSection.VisibleWhen.In!);
    }

    [Fact]
    public void GenerateFormDescriptor_ResourcePool_IncludesMaxField()
    {
        var schema = CreateTestSchema();

        var descriptor = _engine.GenerateFormDescriptor(schema);

        var hpField = descriptor.Sections[2].Fields[0];
        Assert.Equal("hp_max", hpField.MaxField);
    }

    [Fact]
    public void GenerateFormDescriptor_EmptySchema_ReturnsEmptyDescriptor()
    {
        var schema = new CharacterSchema { Sections = [] };

        var descriptor = _engine.GenerateFormDescriptor(schema);

        Assert.Empty(descriptor.Sections);
    }

    #endregion

    #region MapImportedFields Tests

    [Fact]
    public void MapImportedFields_ExactMatch_MapsCorrectly()
    {
        var schema = CreateTestSchema();
        var extracted = new Dictionary<string, string>
        {
            ["name"] = "Gandalf",
            ["level"] = "10",
            ["str"] = "14"
        };

        var result = _engine.MapImportedFields(extracted, schema);

        Assert.Equal("Gandalf", result.MappedFields["name"]);
        Assert.Equal("10", result.MappedFields["level"]);
        Assert.Equal("14", result.MappedFields["str"]);
        Assert.Empty(result.UnmappedFields);
    }

    [Fact]
    public void MapImportedFields_LabelMatch_MapsCorrectly()
    {
        var schema = CreateTestSchema();
        var extracted = new Dictionary<string, string>
        {
            ["Character Name"] = "Gandalf",
            ["Strength"] = "14"
        };

        var result = _engine.MapImportedFields(extracted, schema);

        Assert.Equal("Gandalf", result.MappedFields["name"]);
        Assert.Equal("14", result.MappedFields["str"]);
    }

    [Fact]
    public void MapImportedFields_CaseInsensitive_MapsCorrectly()
    {
        var schema = CreateTestSchema();
        var extracted = new Dictionary<string, string>
        {
            ["NAME"] = "Gandalf",
            ["LEVEL"] = "10"
        };

        var result = _engine.MapImportedFields(extracted, schema);

        Assert.Equal("Gandalf", result.MappedFields["name"]);
        Assert.Equal("10", result.MappedFields["level"]);
    }

    [Fact]
    public void MapImportedFields_UnmatchedFields_GoToUnmapped()
    {
        var schema = CreateTestSchema();
        var extracted = new Dictionary<string, string>
        {
            ["name"] = "Gandalf",
            ["favorite_color"] = "Grey"
        };

        var result = _engine.MapImportedFields(extracted, schema);

        Assert.Equal("Gandalf", result.MappedFields["name"]);
        Assert.Equal("Grey", result.UnmappedFields["favorite_color"]);
    }

    [Fact]
    public void MapImportedFields_EmptyExtracted_ReturnsEmpty()
    {
        var schema = CreateTestSchema();
        var extracted = new Dictionary<string, string>();

        var result = _engine.MapImportedFields(extracted, schema);

        Assert.Empty(result.MappedFields);
        Assert.Empty(result.UnmappedFields);
    }

    [Fact]
    public void MapImportedFields_NormalizedMatch_HandlesUnderscoresAndSpaces()
    {
        var schema = CreateTestSchema();
        var extracted = new Dictionary<string, string>
        {
            ["character-name"] = "Gandalf",
            ["hit dice"] = "1d10"
        };

        var result = _engine.MapImportedFields(extracted, schema);

        Assert.Equal("Gandalf", result.MappedFields["name"]);
        Assert.Equal("1d10", result.MappedFields["hit_dice"]);
    }

    #endregion
}
