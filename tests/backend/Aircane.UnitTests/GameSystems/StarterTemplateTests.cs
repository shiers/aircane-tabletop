using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems.Seeds;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for starter templates. Verifies all 5 templates pass validation
/// and produce expected dice resolution behavior.
/// </summary>
public class StarterTemplateTests
{
    private readonly GameSystemDefinitionSerializer _serializer = new();
    private readonly GameSystemDefinitionValidator _validator = new();
    private readonly MechanicResolver _resolver = new();

    // ── Validation Tests ──────────────────────────────────────────────────────

    [Fact]
    public void AllTemplates_ReturnsFiveTemplates()
    {
        var templates = StarterTemplates.GetAll();
        Assert.Equal(5, templates.Count);
    }

    [Fact]
    public void D20SystemTemplate_PassesValidation()
    {
        var template = StarterTemplates.D20System();
        var definition = ParseAndValidate(template.DefinitionJson);
        Assert.NotNull(definition);
        Assert.Equal("my-d20-system", definition.Identifier);
        Assert.Equal("My d20 System", definition.Name);
    }

    [Fact]
    public void DicePoolTemplate_PassesValidation()
    {
        var template = StarterTemplates.DicePool();
        var definition = ParseAndValidate(template.DefinitionJson);
        Assert.NotNull(definition);
        Assert.Equal("my-dice-pool-system", definition.Identifier);
        Assert.Equal("My Dice Pool System", definition.Name);
    }

    [Fact]
    public void PbtATemplate_PassesValidation()
    {
        var template = StarterTemplates.PbtA();
        var definition = ParseAndValidate(template.DefinitionJson);
        Assert.NotNull(definition);
        Assert.Equal("my-pbta-system", definition.Identifier);
        Assert.Equal("My PbtA System", definition.Name);
    }

    [Fact]
    public void PercentileTemplate_PassesValidation()
    {
        var template = StarterTemplates.Percentile();
        var definition = ParseAndValidate(template.DefinitionJson);
        Assert.NotNull(definition);
        Assert.Equal("my-percentile-system", definition.Identifier);
        Assert.Equal("My Percentile System", definition.Name);
    }

    [Fact]
    public void FreeformTemplate_PassesValidation()
    {
        var template = StarterTemplates.Freeform();
        var definition = ParseAndValidate(template.DefinitionJson);
        Assert.NotNull(definition);
        Assert.Equal("my-freeform-system", definition.Identifier);
        Assert.Equal("My Freeform System", definition.Name);
    }

    [Theory]
    [InlineData("d20-system")]
    [InlineData("dice-pool")]
    [InlineData("pbta")]
    [InlineData("percentile")]
    [InlineData("freeform")]
    public void AllTemplates_HaveUniqueIds(string expectedId)
    {
        var templates = StarterTemplates.GetAll();
        var matching = templates.Where(t => t.Id == expectedId).ToList();
        Assert.Single(matching);
    }

    // ── Dice Resolution Behavior Tests ────────────────────────────────────────

    [Fact]
    public void D20SystemTemplate_ParsesAndRollsDice()
    {
        var template = StarterTemplates.D20System();
        var definition = ParseDefinition(template.DefinitionJson);
        var convention = definition.DiceConventions.First(c => c.Name == "primary");

        // Parse a d20+5 expression
        var parseResult = _resolver.ParseExpression("1d20+5", convention);
        Assert.True(parseResult.IsSuccess);

        // Roll it
        var random = new FixedRandomSource(15); // Always rolls 15
        var roll = _resolver.Roll(parseResult.Expression!, convention, random);

        Assert.NotEmpty(roll.RawResults);
        Assert.Equal(15, roll.RawResults[0]);
        Assert.Equal(20, roll.Total); // 15 + 5
    }

    [Fact]
    public void DicePoolTemplate_ParsesAndCountsSuccesses()
    {
        var template = StarterTemplates.DicePool();
        var definition = ParseDefinition(template.DefinitionJson);
        var convention = definition.DiceConventions.First(c => c.Name == "primary");

        // Parse a pool expression: 5d6>=5
        var parseResult = _resolver.ParseExpression("5d6>=5", convention);
        Assert.True(parseResult.IsSuccess);

        // Roll with known values: [1, 3, 5, 6, 2] -> 2 successes (5 and 6)
        var random = new SequenceRandomSource([1, 3, 5, 6, 2]);
        var roll = _resolver.Roll(parseResult.Expression!, convention, random);

        Assert.NotNull(roll.SuccessCount);
        Assert.Equal(2, roll.SuccessCount);
    }

    [Fact]
    public void PbtATemplate_ParsesAndRollsDice()
    {
        var template = StarterTemplates.PbtA();
        var definition = ParseDefinition(template.DefinitionJson);
        var convention = definition.DiceConventions.First(c => c.Name == "primary");

        // Parse 2d6+1
        var parseResult = _resolver.ParseExpression("2d6+1", convention);
        Assert.True(parseResult.IsSuccess);

        // Roll with known values: [4, 5] -> total 10 (4+5+1)
        var random = new SequenceRandomSource([4, 5]);
        var roll = _resolver.Roll(parseResult.Expression!, convention, random);

        Assert.Equal(10, roll.Total);
        Assert.NotEmpty(roll.RawResults);
    }

    [Fact]
    public void PercentileTemplate_ParsesAndRollsD100()
    {
        var template = StarterTemplates.Percentile();
        var definition = ParseDefinition(template.DefinitionJson);
        var convention = definition.DiceConventions.First(c => c.Name == "primary");

        // Parse 1d100
        var parseResult = _resolver.ParseExpression("1d100", convention);
        Assert.True(parseResult.IsSuccess);

        // Roll with known value: 42
        var random = new FixedRandomSource(42);
        var roll = _resolver.Roll(parseResult.Expression!, convention, random);

        Assert.Equal(42, roll.Total);
        Assert.Single(roll.RawResults);
        Assert.Equal(42, roll.RawResults[0]);
    }

    [Fact]
    public void FreeformTemplate_HasNoDiceConventions()
    {
        var template = StarterTemplates.Freeform();
        var definition = ParseDefinition(template.DefinitionJson);

        // Freeform has no dice conventions
        Assert.Empty(definition.DiceConventions);
        Assert.Empty(definition.ResolutionRules);
    }

    [Fact]
    public void D20SystemTemplate_HasCharacterSchema()
    {
        var template = StarterTemplates.D20System();
        var definition = ParseDefinition(template.DefinitionJson);

        Assert.NotNull(definition.CharacterSchema);
        Assert.NotEmpty(definition.CharacterSchema.Sections);

        // Should have basics, abilities, and combat sections
        var sectionIds = definition.CharacterSchema.Sections.Select(s => s.Id).ToList();
        Assert.Contains("basics", sectionIds);
        Assert.Contains("abilities", sectionIds);
        Assert.Contains("combat", sectionIds);
    }

    [Fact]
    public void D20SystemTemplate_HasConditionSet()
    {
        var template = StarterTemplates.D20System();
        var definition = ParseDefinition(template.DefinitionJson);

        Assert.NotEmpty(definition.ConditionSet);
        var conditionNames = definition.ConditionSet.Select(c => c.Name).ToList();
        Assert.Contains("Poisoned", conditionNames);
        Assert.Contains("Prone", conditionNames);
    }

    [Fact]
    public void D20SystemTemplate_HasActionEconomy()
    {
        var template = StarterTemplates.D20System();
        var definition = ParseDefinition(template.DefinitionJson);

        Assert.NotNull(definition.ActionEconomy);
        Assert.NotNull(definition.ActionEconomy.TurnStructure);
        Assert.NotEmpty(definition.ActionEconomy.TurnStructure.Slots);
    }

    [Fact]
    public void DicePoolTemplate_HasActionPoints()
    {
        var template = StarterTemplates.DicePool();
        var definition = ParseDefinition(template.DefinitionJson);

        Assert.NotNull(definition.ActionEconomy);
        Assert.Equal(ActionEconomyType.ActionPoints, definition.ActionEconomy.Type);
    }

    [Fact]
    public void PbtATemplate_HasFreeformActionEconomy()
    {
        var template = StarterTemplates.PbtA();
        var definition = ParseDefinition(template.DefinitionJson);

        Assert.NotNull(definition.ActionEconomy);
        Assert.Equal(ActionEconomyType.Freeform, definition.ActionEconomy.Type);
    }

    [Fact]
    public void AllTemplates_HaveAiGuidance()
    {
        var templates = StarterTemplates.GetAll();
        foreach (var template in templates)
        {
            var definition = ParseDefinition(template.DefinitionJson);
            Assert.NotNull(definition.AiGuidance);
            Assert.False(string.IsNullOrWhiteSpace(definition.AiGuidance.SystemPromptNotes));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GameSystemDefinition ParseAndValidate(string json)
    {
        var parseResult = _serializer.ParseJson(json);
        Assert.True(parseResult.IsSuccess, $"Parse failed: {string.Join(", ", parseResult.Errors.Select(e => e.Message))}");

        var definition = parseResult.Definition!;
        var validationResult = _validator.Validate(definition);
        Assert.True(validationResult.IsValid, $"Validation failed: {string.Join(", ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");

        return definition;
    }

    private GameSystemDefinition ParseDefinition(string json)
    {
        var parseResult = _serializer.ParseJson(json);
        Assert.True(parseResult.IsSuccess, $"Parse failed: {string.Join(", ", parseResult.Errors.Select(e => e.Message))}");
        return parseResult.Definition!;
    }

    /// <summary>
    /// A random source that always returns the same value (adjusted for the range).
    /// </summary>
    private sealed class FixedRandomSource : IRandomSource
    {
        private readonly int _value;

        public FixedRandomSource(int value) => _value = value;

        public int Next(int minInclusive, int maxExclusive)
        {
            // Clamp the value to the valid range
            return Math.Max(minInclusive, Math.Min(_value, maxExclusive - 1));
        }
    }

    /// <summary>
    /// A random source that returns values from a predefined sequence.
    /// </summary>
    private sealed class SequenceRandomSource : IRandomSource
    {
        private readonly int[] _values;
        private int _index;

        public SequenceRandomSource(int[] values)
        {
            _values = values;
            _index = 0;
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            var value = _values[_index % _values.Length];
            _index++;
            return Math.Max(minInclusive, Math.Min(value, maxExclusive - 1));
        }
    }
}
