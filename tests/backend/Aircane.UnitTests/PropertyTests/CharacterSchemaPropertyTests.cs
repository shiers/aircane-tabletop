using System.Text.Json;
using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for the CharacterSchemaEngine.
/// Tests Properties 12 and 13 from the design document.
/// </summary>
public class CharacterSchemaPropertyTests
{
    private static readonly CharacterSchemaEngine Engine = new();

    // ══════════════════════════════════════════════════════════════════════════
    // Property 12: Character Schema validation and form generation
    // **Validates: Requirements 4.1, 4.3**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 12: a valid Character Schema.
    /// </summary>
    public record SchemaFormTestInput(CharacterSchema Schema);

    /// <summary>
    /// Generates a valid CharacterSchemaField with a random type.
    /// </summary>
    private static Gen<CharacterSchemaField> FieldGen =>
        from id in Gen.Elements("field_a", "field_b", "field_c", "field_d", "field_e", "field_f", "field_g", "field_h")
        from type in Gen.Elements(
            CharacterFieldType.Text,
            CharacterFieldType.Number,
            CharacterFieldType.Boolean,
            CharacterFieldType.Enum,
            CharacterFieldType.DiceExpression,
            CharacterFieldType.List,
            CharacterFieldType.Calculated,
            CharacterFieldType.ResourcePool)
        from label in Gen.Elements("Label A", "Label B", "Label C", "Label D")
        from required in Arb.Generate<bool>()
        from hasMin in Arb.Generate<bool>()
        from minVal in Gen.Choose(1, 10)
        from hasMax in Arb.Generate<bool>()
        from maxVal in Gen.Choose(11, 30)
        let min = type == CharacterFieldType.Number && hasMin ? (int?)minVal : null
        let max = type == CharacterFieldType.Number && hasMax ? (int?)maxVal : null
        let options = type == CharacterFieldType.Enum ? (IReadOnlyList<string>)new[] { "Opt1", "Opt2", "Opt3" } : null
        let formula = type == CharacterFieldType.Calculated ? "field_a + 1" : null
        select new CharacterSchemaField
        {
            Id = $"{id}_{(int)type}",
            Type = type,
            Label = label,
            Required = required,
            Min = min,
            Max = max,
            Options = options,
            Formula = formula
        };

    /// <summary>
    /// Generates a valid CharacterSchemaSection with 1-4 fields.
    /// </summary>
    private static Gen<CharacterSchemaSection> SectionGen =>
        from id in Gen.Elements("section_1", "section_2", "section_3")
        from label in Gen.Elements("Section One", "Section Two", "Section Three")
        from fieldCount in Gen.Choose(1, 4)
        from fields in Gen.ListOf(fieldCount, FieldGen)
        // Ensure unique field IDs within a section
        let uniqueFields = fields.GroupBy(f => f.Id).Select(g => g.First()).ToList()
        select new CharacterSchemaSection
        {
            Id = id,
            Label = label,
            Fields = uniqueFields
        };

    /// <summary>
    /// Generates a valid CharacterSchema with 1-3 sections.
    /// </summary>
    private static Gen<SchemaFormTestInput> SchemaFormTestInputGen =>
        from sectionCount in Gen.Choose(1, 3)
        from sections in Gen.ListOf(sectionCount, SectionGen)
        // Ensure unique section IDs
        let uniqueSections = sections.GroupBy(s => s.Id).Select(g => g.First()).ToList()
        select new SchemaFormTestInput(new CharacterSchema { Sections = uniqueSections });

    public static Arbitrary<SchemaFormTestInput> SchemaFormTestInputArbitrary =>
        Arb.From(SchemaFormTestInputGen);

    /// <summary>
    /// Property 12 (Part 1): For any valid Character Schema, the form descriptor SHALL contain
    /// an entry for every field defined in the schema with the correct type and label.
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CharacterSchemaPropertyTests) }, MaxTest = 200)]
    public void FormDescriptor_ContainsEntryForEveryField_WithCorrectTypeAndLabel(SchemaFormTestInput input)
    {
        var descriptor = Engine.GenerateFormDescriptor(input.Schema);

        // The descriptor must have the same number of sections
        Assert.Equal(input.Schema.Sections.Count, descriptor.Sections.Count);

        for (var i = 0; i < input.Schema.Sections.Count; i++)
        {
            var schemaSection = input.Schema.Sections[i];
            var formSection = descriptor.Sections[i];

            // Section ID and label must match
            Assert.Equal(schemaSection.Id, formSection.Id);
            Assert.Equal(schemaSection.Label, formSection.Label);

            // Must have same number of fields
            Assert.Equal(schemaSection.Fields.Count, formSection.Fields.Count);

            for (var j = 0; j < schemaSection.Fields.Count; j++)
            {
                var schemaField = schemaSection.Fields[j];
                var formField = formSection.Fields[j];

                // Field ID, type, and label must match
                Assert.Equal(schemaField.Id, formField.Id);
                Assert.Equal(schemaField.Type, formField.Type);
                Assert.Equal(schemaField.Label, formField.Label);

                // Validation rules must be preserved
                Assert.Equal(schemaField.Required, formField.Required);
                Assert.Equal(schemaField.Min, formField.Min);
                Assert.Equal(schemaField.Max, formField.Max);

                // Calculated fields must be read-only
                if (schemaField.Type == CharacterFieldType.Calculated)
                {
                    Assert.True(formField.IsReadOnly);
                }
            }
        }
    }

    // ── Property 12 (Part 2): Validation accepts valid data, rejects invalid data ──

    /// <summary>
    /// Test input for validation property: a schema with number fields that have min/max constraints.
    /// </summary>
    public record ValidationTestInput(
        CharacterSchema Schema,
        string ValidJson,
        string InvalidJson,
        string ViolatedFieldPath);

    /// <summary>
    /// Generates a schema with a required number field with min/max, plus valid and invalid JSON.
    /// </summary>
    private static Gen<ValidationTestInput> ValidationTestInputGen =>
        from fieldId in Gen.Elements("score", "level", "hp", "ac")
        from minVal in Gen.Choose(1, 10)
        from maxVal in Gen.Choose(11, 30)
        from validValue in Gen.Choose(minVal, maxVal)
        from invalidLow in Gen.Choose(-10, minVal - 1)
        let schema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "test",
                    Label = "Test",
                    Fields =
                    [
                        new CharacterSchemaField
                        {
                            Id = fieldId,
                            Type = CharacterFieldType.Number,
                            Required = true,
                            Min = minVal,
                            Max = maxVal
                        }
                    ]
                }
            ]
        }
        let validJson = $"{{\"{fieldId}\": {validValue}}}"
        let invalidJson = $"{{\"{fieldId}\": {invalidLow}}}"
        select new ValidationTestInput(schema, validJson, invalidJson, fieldId);

    public static Arbitrary<ValidationTestInput> ValidationTestInputArbitrary =>
        Arb.From(ValidationTestInputGen);

    /// <summary>
    /// Property 12 (Part 2): For any character JSON and schema with validation rules,
    /// the validator SHALL accept data satisfying all rules and reject data violating any rule.
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CharacterSchemaPropertyTests) }, MaxTest = 200)]
    public void Validator_AcceptsValidData_RejectsInvalidData(ValidationTestInput input)
    {
        // Valid data should pass
        var validResult = Engine.Validate(input.ValidJson, input.Schema);
        Assert.True(validResult.IsValid,
            $"Expected valid JSON to pass validation. Errors: {string.Join("; ", validResult.Errors.Select(e => e.Message))}");

        // Invalid data should fail
        var invalidResult = Engine.Validate(input.InvalidJson, input.Schema);
        Assert.False(invalidResult.IsValid,
            "Expected invalid JSON to fail validation");
        Assert.Contains(invalidResult.Errors, e => e.FieldPath == input.ViolatedFieldPath);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 13: Calculated field computation correctness
    // **Validates: Requirements 4.4**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 13: a schema with calculated fields and source values.
    /// </summary>
    public record CalculatedFieldTestInput(
        string FieldId,
        string Formula,
        Dictionary<string, double> SourceValues,
        double ExpectedResult);

    /// <summary>
    /// Generates test inputs for calculated field computation.
    /// Uses simple formulas that we can independently verify.
    /// </summary>
    private static Gen<CalculatedFieldTestInput> CalculatedFieldTestInputGen =>
        Gen.OneOf(
            // Formula: floor((source - 10) / 2) - D&D ability modifier
            from sourceValue in Gen.Choose(1, 30)
            let expected = Math.Floor((sourceValue - 10.0) / 2.0)
            select new CalculatedFieldTestInput(
                "ability_mod",
                "floor((source - 10) / 2)",
                new Dictionary<string, double> { ["source"] = sourceValue },
                expected),

            // Formula: a + b - simple addition
            from a in Gen.Choose(-20, 20)
            from b in Gen.Choose(-20, 20)
            let expected2 = (double)(a + b)
            select new CalculatedFieldTestInput(
                "sum_field",
                "a + b",
                new Dictionary<string, double> { ["a"] = a, ["b"] = b },
                expected2),

            // Formula: a * b + c - multiplication and addition
            from a in Gen.Choose(1, 10)
            from b in Gen.Choose(1, 10)
            from c in Gen.Choose(-5, 5)
            let expected3 = (double)(a * b + c)
            select new CalculatedFieldTestInput(
                "complex_field",
                "a * b + c",
                new Dictionary<string, double> { ["a"] = a, ["b"] = b, ["c"] = c },
                expected3),

            // Formula: ceil(x / 2) - ceiling division
            from x in Gen.Choose(1, 20)
            let expected4 = Math.Ceiling(x / 2.0)
            select new CalculatedFieldTestInput(
                "ceil_field",
                "ceil(x / 2)",
                new Dictionary<string, double> { ["x"] = x },
                expected4)
        );

    public static Arbitrary<CalculatedFieldTestInput> CalculatedFieldTestInputArbitrary =>
        Arb.From(CalculatedFieldTestInputGen);

    /// <summary>
    /// Property 13: For any Character Schema with calculated fields and character data providing
    /// all source field values, the computed value of each calculated field SHALL equal the result
    /// of evaluating its formula against the source values.
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CharacterSchemaPropertyTests) }, MaxTest = 300)]
    public void CalculatedField_ComputedValue_EqualsFormulaEvaluation(CalculatedFieldTestInput input)
    {
        // Build a schema with the calculated field
        var schema = new CharacterSchema
        {
            Sections =
            [
                new CharacterSchemaSection
                {
                    Id = "test",
                    Label = "Test",
                    Fields = input.SourceValues.Keys
                        .Select(k => new CharacterSchemaField { Id = k, Type = CharacterFieldType.Number })
                        .Append(new CharacterSchemaField
                        {
                            Id = input.FieldId,
                            Type = CharacterFieldType.Calculated,
                            Formula = input.Formula
                        })
                        .ToList()
                }
            ]
        };

        // Build character JSON from source values
        var jsonObj = new Dictionary<string, object>();
        foreach (var (key, value) in input.SourceValues)
        {
            jsonObj[key] = value;
        }
        var characterJson = JsonSerializer.Serialize(jsonObj);

        // Compute calculated fields
        var result = Engine.ComputeCalculatedFields(characterJson, schema);

        // The computed value must match the expected result
        Assert.True(result.ContainsKey(input.FieldId),
            $"Expected computed fields to contain '{input.FieldId}'");

        Assert.NotNull(result[input.FieldId]);
        var computedValue = Convert.ToDouble(result[input.FieldId]);

        Assert.Equal(input.ExpectedResult, computedValue, precision: 10);
    }
}
