using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for the AiContextAdapter.
/// Tests system context building, roll request formatting, and action validation.
/// </summary>
public class AiContextAdapterTests
{
    private readonly AiContextAdapter _adapter = new();

    // ── BuildSystemContext Tests ─────────────────────────────────────────────

    [Fact]
    public void BuildSystemContext_IncludesSystemName()
    {
        var definition = CreateMinimalDefinition("Test System");

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("Test System", context);
    }

    [Fact]
    public void BuildSystemContext_IncludesDiceConventions()
    {
        var definition = CreateD20Definition();

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("Dice Conventions", context);
        Assert.Contains("primary", context);
        Assert.Contains("Single Die + Modifier", context);
    }

    [Fact]
    public void BuildSystemContext_IncludesResolutionRules()
    {
        var definition = CreateD20Definition();

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("Resolution Rules", context);
        Assert.Contains("abilityCheck", context);
    }

    [Fact]
    public void BuildSystemContext_IncludesAiGuidance()
    {
        var definition = CreateD20Definition();
        definition.AiGuidance = new AiGuidance
        {
            SystemPromptNotes = "This is a d20-based fantasy RPG.",
            ToneGuidance = "High fantasy, heroic",
            MechanicalNotes = "Always use d20+modifier vs DC.",
            CommonMistakes = ["Do not use degrees of success."],
            RollFormatExample = "1d20+{modifier}"
        };

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("AI Guidance", context);
        Assert.Contains("d20-based fantasy RPG", context);
        Assert.Contains("High fantasy, heroic", context);
        Assert.Contains("Always use d20+modifier vs DC", context);
        Assert.Contains("Do not use degrees of success", context);
        Assert.Contains("1d20+{modifier}", context);
    }

    [Fact]
    public void BuildSystemContext_IncludesActionEconomy()
    {
        var definition = CreateD20Definition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots =
                [
                    new ActionSlot { Name = "action", Label = "Action", Count = 1 },
                    new ActionSlot { Name = "bonus_action", Label = "Bonus Action", Count = 1 }
                ]
            }
        };

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("Action Economy", context);
        Assert.Contains("Action", context);
        Assert.Contains("Bonus Action", context);
    }

    [Fact]
    public void BuildSystemContext_IncludesConditions()
    {
        var definition = CreateD20Definition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "Poisoned", Description = "Disadvantage on attacks" },
            new ConditionDefinition { Name = "Prone", Description = "Disadvantage on attack rolls" }
        ];

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("Available Conditions", context);
        Assert.Contains("Poisoned", context);
        Assert.Contains("Prone", context);
    }

    [Fact]
    public void BuildSystemContext_IncompleteDefinition_AddsNote()
    {
        var definition = new GameSystemDefinition(
            "incomplete", "Incomplete System", "1.0.0", 1, "user-created");
        // No dice conventions or resolution rules

        var context = _adapter.BuildSystemContext(definition);

        Assert.Contains("incomplete", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask the host for clarification", context);
    }

    // ── FormatRollRequest Tests ──────────────────────────────────────────────

    [Fact]
    public void FormatRollRequest_SingleDieModifier_UsesFormulaAsIs()
    {
        var convention = new DiceConvention
        {
            Type = DiceConventionType.SingleDieModifier,
            Name = "primary",
            Die = "d20"
        };

        var result = _adapter.FormatRollRequest(convention, "Strength Check", "1d20+5");

        Assert.Equal("Strength Check", result.Label);
        Assert.Equal("1d20+5", result.Formula);
        Assert.Equal("SingleDieModifier", result.ConventionType);
    }

    [Fact]
    public void FormatRollRequest_DicePool_AddsSuccessThreshold()
    {
        var convention = new DiceConvention
        {
            Type = DiceConventionType.DicePoolSuccess,
            Name = "primary",
            Die = "d6",
            SuccessThreshold = 5
        };

        var result = _adapter.FormatRollRequest(convention, "Hacking", "8d6");

        Assert.Equal("Hacking", result.Label);
        Assert.Equal("8d6>=5", result.Formula);
        Assert.Equal("DicePoolSuccess", result.ConventionType);
    }

    [Fact]
    public void FormatRollRequest_DicePool_PreservesExistingThreshold()
    {
        var convention = new DiceConvention
        {
            Type = DiceConventionType.DicePoolSuccess,
            Name = "primary",
            Die = "d10",
            SuccessThreshold = 8
        };

        var result = _adapter.FormatRollRequest(convention, "Perception", "6d10>=8");

        Assert.Equal("6d10>=8", result.Formula);
    }

    [Fact]
    public void FormatRollRequest_Fudge_UsesFudgeNotation()
    {
        var convention = new DiceConvention
        {
            Type = DiceConventionType.Fudge,
            Name = "primary",
            Die = "dF"
        };

        var result = _adapter.FormatRollRequest(convention, "Overcome", "4dF+2");

        Assert.Equal("4dF+2", result.Formula);
        Assert.Equal("Fudge", result.ConventionType);
    }

    [Fact]
    public void FormatRollRequest_Percentile_UsesD100()
    {
        var convention = new DiceConvention
        {
            Type = DiceConventionType.Percentile,
            Name = "primary",
            Die = "d100"
        };

        var result = _adapter.FormatRollRequest(convention, "Skill Check", "50");

        Assert.Equal("1d100", result.Formula);
        Assert.Equal("Percentile", result.ConventionType);
    }

    [Fact]
    public void FormatRollRequest_WithResolutionRule_IncludesDescription()
    {
        var convention = new DiceConvention
        {
            Type = DiceConventionType.SingleDieModifier,
            Name = "primary",
            Die = "d20"
        };

        var rule = new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Comparison = ">=",
            TargetSource = "dc"
        };

        var result = _adapter.FormatRollRequest(convention, "Wisdom Save", "1d20+3", rule);

        Assert.NotNull(result.ResolutionDescription);
        Assert.Contains(">=", result.ResolutionDescription);
        Assert.Contains("dc", result.ResolutionDescription, StringComparison.OrdinalIgnoreCase);
    }

    // ── ValidateProposedActions Tests ────────────────────────────────────────

    [Fact]
    public void ValidateProposedActions_ValidCondition_ReturnsValid()
    {
        var definition = CreateD20Definition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "Poisoned", Description = "Disadvantage" }
        ];

        var actions = new List<AiProposedAction>
        {
            new() { ActionType = "action", ConditionName = "Poisoned" }
        };

        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
            }
        };

        var result = _adapter.ValidateProposedActions(actions, definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProposedActions_InvalidCondition_ReturnsError()
    {
        var definition = CreateD20Definition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "Poisoned", Description = "Disadvantage" }
        ];

        var actions = new List<AiProposedAction>
        {
            new() { ActionType = "action", ConditionName = "Confused" }
        };

        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
            }
        };

        var result = _adapter.ValidateProposedActions(actions, definition);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Confused", result.Errors[0]);
    }

    [Fact]
    public void ValidateProposedActions_InvalidActionType_ReturnsError()
    {
        var definition = CreateD20Definition();
        definition.ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
            }
        };

        var actions = new List<AiProposedAction>
        {
            new() { ActionType = "legendary_action" }
        };

        var result = _adapter.ValidateProposedActions(actions, definition);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("legendary_action", result.Errors[0]);
    }

    [Fact]
    public void ValidateProposedActions_NoConditionSet_SkipsConditionValidation()
    {
        var definition = CreateD20Definition();
        // No conditions defined

        var actions = new List<AiProposedAction>
        {
            new() { ActionType = "attack", ConditionName = "AnyCondition" }
        };

        var result = _adapter.ValidateProposedActions(actions, definition);

        // Should be valid since no condition set is defined (freeform)
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateProposedActions_NoActionEconomy_SkipsActionTypeValidation()
    {
        var definition = CreateD20Definition();
        definition.ConditionSet =
        [
            new ConditionDefinition { Name = "Poisoned", Description = "Disadvantage" }
        ];

        var actions = new List<AiProposedAction>
        {
            new() { ActionType = "any_action", ConditionName = "Poisoned" }
        };

        var result = _adapter.ValidateProposedActions(actions, definition);

        // Should be valid since no action economy is defined
        Assert.True(result.IsValid);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static GameSystemDefinition CreateMinimalDefinition(string name)
    {
        return new GameSystemDefinition(
            name.ToLowerInvariant().Replace(" ", "-"),
            name,
            "1.0.0",
            1,
            "user-created");
    }

    private static GameSystemDefinition CreateD20Definition()
    {
        var definition = new GameSystemDefinition(
            "dnd-5e-2014",
            "D&D 5e 2014",
            "1.0.0",
            1,
            "built-in",
            "Wizards of the Coast",
            "fantasy",
            "The 2014 core rules for D&D 5e.");

        definition.DiceConventions =
        [
            new DiceConvention
            {
                Type = DiceConventionType.SingleDieModifier,
                Name = "primary",
                Die = "d20",
                Description = "Roll d20 + modifier vs DC",
                ModifierSources = ["ability_modifier", "proficiency_bonus"]
            }
        ];

        definition.ResolutionRules =
        [
            new ResolutionRule
            {
                Name = "abilityCheck",
                Type = ResolutionRuleType.TargetNumber,
                Roll = "primary",
                Comparison = ">=",
                TargetSource = "dc",
                CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
                CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
            }
        ];

        return definition;
    }
}
