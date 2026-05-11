using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class ConditionRegistryTests
{
    private readonly ConditionRegistry _registry = new();

    private static IReadOnlyList<ConditionDefinition> Dnd5eConditions =>
    [
        new ConditionDefinition
        {
            Name = "Poisoned",
            Description = "Disadvantage on attack rolls and ability checks.",
            Effects =
            [
                new ConditionEffect { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new ConditionEffect { Type = "roll_modifier", Scope = "ability_checks", Effect = "disadvantage" }
            ],
            DurationType = "until_save",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Prone",
            Description = "Disadvantage on attack rolls. Melee attacks against have advantage.",
            Effects =
            [
                new ConditionEffect { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new ConditionEffect { Type = "grant_advantage", Scope = "melee_attacks_against" }
            ],
            DurationType = "until_action",
            EndCondition = "Use half movement to stand",
            Stackable = false
        },
        new ConditionDefinition
        {
            Name = "Stunned",
            Description = "Can't take actions or reactions. Auto-fail STR and DEX saves.",
            Effects =
            [
                new ConditionEffect { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" },
                new ConditionEffect { Type = "grant_advantage", Scope = "attacks_against" }
            ],
            DurationType = "rounds",
            Stackable = false
        }
    ];

    // ── GetConditions ─────────────────────────────────────────────────────────

    [Fact]
    public void GetConditions_ReturnsAllConditionsFromSet()
    {
        var result = _registry.GetConditions(Dnd5eConditions);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, c => c.Name == "Poisoned");
        Assert.Contains(result, c => c.Name == "Prone");
        Assert.Contains(result, c => c.Name == "Stunned");
    }

    [Fact]
    public void GetConditions_EmptySet_ReturnsEmptyList()
    {
        var result = _registry.GetConditions([]);

        Assert.Empty(result);
    }

    // ── IsValidCondition ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Poisoned", true)]
    [InlineData("Prone", true)]
    [InlineData("Stunned", true)]
    [InlineData("poisoned", true)]  // Case-insensitive
    [InlineData("PRONE", true)]     // Case-insensitive
    [InlineData("Invisible", false)]
    [InlineData("Unknown", false)]
    public void IsValidCondition_WithDefinedSet_ValidatesCorrectly(string name, bool expected)
    {
        var result = _registry.IsValidCondition(Dnd5eConditions, name);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValidCondition_FreeformMode_AcceptsAnyName()
    {
        Assert.True(_registry.IsValidCondition([], "AnyCondition"));
        Assert.True(_registry.IsValidCondition([], "CustomEffect"));
        Assert.True(_registry.IsValidCondition([], "Homebrew Status"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidCondition_EmptyOrNullName_ReturnsFalse(string? name)
    {
        Assert.False(_registry.IsValidCondition(Dnd5eConditions, name!));
        Assert.False(_registry.IsValidCondition([], name!));
    }

    // ── GetCondition ──────────────────────────────────────────────────────────

    [Fact]
    public void GetCondition_ExistingCondition_ReturnsDefinition()
    {
        var result = _registry.GetCondition(Dnd5eConditions, "Poisoned");

        Assert.NotNull(result);
        Assert.Equal("Poisoned", result.Name);
        Assert.Equal(2, result.Effects.Count);
        Assert.Contains(result.Effects, e => e.Scope == "attack_rolls" && e.Effect == "disadvantage");
    }

    [Fact]
    public void GetCondition_CaseInsensitive_ReturnsDefinition()
    {
        var result = _registry.GetCondition(Dnd5eConditions, "poisoned");

        Assert.NotNull(result);
        Assert.Equal("Poisoned", result.Name);
    }

    [Fact]
    public void GetCondition_UnknownCondition_ReturnsNull()
    {
        var result = _registry.GetCondition(Dnd5eConditions, "Invisible");

        Assert.Null(result);
    }

    [Fact]
    public void GetCondition_FreeformMode_ReturnsNull()
    {
        // In freeform mode, no mechanical effects are available
        var result = _registry.GetCondition([], "AnyCondition");

        Assert.Null(result);
    }

    [Fact]
    public void GetCondition_ReturnsEffectsWithCorrectStructure()
    {
        var result = _registry.GetCondition(Dnd5eConditions, "Prone");

        Assert.NotNull(result);
        Assert.Equal("until_action", result.DurationType);
        Assert.Equal("Use half movement to stand", result.EndCondition);
        Assert.False(result.Stackable);
        Assert.Equal(2, result.Effects.Count);
    }
}
