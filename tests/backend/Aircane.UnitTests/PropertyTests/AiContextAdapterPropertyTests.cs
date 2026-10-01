using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for the AiContextAdapter.
/// Property 17: AI context adapter includes system mechanics.
/// Property 18: AI roll request uses active convention format.
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.5**
/// </summary>
public class AiContextAdapterPropertyTests
{
    private static readonly AiContextAdapter Adapter = new();

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Test input for Property 17: a definition with dice conventions, resolution rules, and AI guidance.
    /// </summary>
    public record SystemContextInput(
        string SystemName,
        DiceConventionType ConventionType,
        string ConventionName,
        ResolutionRuleType RuleType,
        string RuleName,
        string GuidanceText);

    /// <summary>
    /// Generates a valid system context input with non-empty fields.
    /// </summary>
    private static Gen<SystemContextInput> SystemContextInputGen =>
        from systemName in Gen.Elements("Fantasy RPG", "Sci-Fi Game", "Horror System", "Superhero RPG")
        from conventionType in Gen.Elements(
            DiceConventionType.SingleDieModifier,
            DiceConventionType.DicePoolSuccess,
            DiceConventionType.FixedDiceThreshold,
            DiceConventionType.Fudge,
            DiceConventionType.StepDice,
            DiceConventionType.Percentile)
        from conventionName in Gen.Elements("primary", "combat", "skill_check")
        from ruleType in Gen.Elements(
            ResolutionRuleType.TargetNumber,
            ResolutionRuleType.Opposed,
            ResolutionRuleType.DegreesOfSuccess,
            ResolutionRuleType.Margin,
            ResolutionRuleType.ThresholdBands)
        from ruleName in Gen.Elements("abilityCheck", "attackRoll", "savingThrow", "skillTest")
        from guidanceText in Gen.Elements(
            "Use dramatic narration for combat.",
            "Always describe the environment before asking for rolls.",
            "This system uses narrative-first resolution.",
            "Emphasize consequences over binary pass/fail.")
        select new SystemContextInput(systemName, conventionType, conventionName, ruleType, ruleName, guidanceText);

    public static Arbitrary<SystemContextInput> SystemContextInputArbitrary =>
        Arb.From(SystemContextInputGen);

    /// <summary>
    /// Test input for Property 18: a dice convention with a formula.
    /// </summary>
    public record RollRequestInput(
        DiceConventionType ConventionType,
        string Die,
        int? SuccessThreshold,
        string Label,
        string Formula);

    /// <summary>
    /// Generates roll request inputs for various convention types.
    /// </summary>
    private static Gen<RollRequestInput> RollRequestInputGen =>
        Gen.OneOf(
            // Dice pool systems
            from threshold in Gen.Choose(4, 8)
            from dieCount in Gen.Choose(2, 12)
            from sides in Gen.Elements(6, 10, 12)
            select new RollRequestInput(
                DiceConventionType.DicePoolSuccess,
                $"d{sides}",
                threshold,
                "Pool Roll",
                $"{dieCount}d{sides}"),

            // Fudge systems
            from count in Gen.Choose(2, 6)
            from modifier in Gen.Choose(-2, 4)
            let formula = modifier == 0 ? $"{count}dF" : $"{count}dF+{modifier}"
            select new RollRequestInput(
                DiceConventionType.Fudge,
                "dF",
                null,
                "Fudge Roll",
                formula),

            // Percentile systems
            Gen.Constant(new RollRequestInput(
                DiceConventionType.Percentile,
                "d100",
                null,
                "Skill Check",
                "75")),

            // Single die modifier (d20)
            from modifier in Gen.Choose(1, 10)
            select new RollRequestInput(
                DiceConventionType.SingleDieModifier,
                "d20",
                null,
                "Ability Check",
                $"1d20+{modifier}")
        );

    public static Arbitrary<RollRequestInput> RollRequestInputArbitrary =>
        Arb.From(RollRequestInputGen);

    // ══════════════════════════════════════════════════════════════════════════
    // Property 17: AI context adapter includes system mechanics
    // **Validates: Requirements 8.1, 8.2, 8.5**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 17: For any Game System Definition with non-empty dice conventions,
    /// resolution rules, and AI guidance notes, the generated AI system context string
    /// SHALL contain references to the dice convention type, resolution approach,
    /// and the AI guidance text.
    /// **Validates: Requirements 8.1, 8.2, 8.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AiContextAdapterPropertyTests) }, MaxTest = 200)]
    public void BuildSystemContext_ContainsDiceConventionType_ResolutionApproach_AndGuidance(
        SystemContextInput input)
    {
        var definition = new GameSystemDefinition(
            input.SystemName.ToLowerInvariant().Replace(" ", "-"),
            input.SystemName,
            "1.0.0",
            1,
            "user-created");

        definition.DiceConventions =
        [
            new DiceConvention
            {
                Type = input.ConventionType,
                Name = input.ConventionName,
                Die = GetDieForType(input.ConventionType),
                Description = $"Primary dice mechanic for {input.SystemName}"
            }
        ];

        definition.ResolutionRules =
        [
            new ResolutionRule
            {
                Name = input.RuleName,
                Type = input.RuleType,
                Roll = input.ConventionName,
                Comparison = ">=",
                TargetSource = "dc"
            }
        ];

        definition.AiGuidance = new AiGuidance
        {
            SystemPromptNotes = input.GuidanceText,
            ToneGuidance = "Appropriate tone for this system"
        };

        var context = Adapter.BuildSystemContext(definition);

        // SHALL contain reference to the dice convention type
        var conventionTypeDesc = GetExpectedConventionTypeString(input.ConventionType);
        Assert.Contains(conventionTypeDesc, context,
            StringComparison.OrdinalIgnoreCase);

        // SHALL contain reference to the resolution approach (rule name)
        Assert.Contains(input.RuleName, context);

        // SHALL contain the AI guidance text
        Assert.Contains(input.GuidanceText, context);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 18: AI roll request uses active convention format
    // **Validates: Requirements 8.3**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 18: For any Game System Definition with a primary dice convention,
    /// roll requests formatted by the AI context adapter SHALL use notation consistent
    /// with that convention type rather than defaulting to d20+modifier.
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AiContextAdapterPropertyTests) }, MaxTest = 200)]
    public void FormatRollRequest_UsesConventionNotation_NotDefaultD20(RollRequestInput input)
    {
        var convention = new DiceConvention
        {
            Type = input.ConventionType,
            Name = "primary",
            Die = input.Die,
            SuccessThreshold = input.SuccessThreshold
        };

        var result = Adapter.FormatRollRequest(convention, input.Label, input.Formula);

        // Convention type should match
        Assert.Equal(input.ConventionType.ToString(), result.ConventionType);

        // Verify convention-specific formatting
        switch (input.ConventionType)
        {
            case DiceConventionType.DicePoolSuccess:
                // Pool notation should include success threshold
                Assert.Contains(">=", result.Formula);
                Assert.Contains(input.SuccessThreshold!.Value.ToString(), result.Formula);
                break;

            case DiceConventionType.Fudge:
                // Fudge notation should use dF
                Assert.Contains("dF", result.Formula, StringComparison.OrdinalIgnoreCase);
                // Should NOT default to d20
                Assert.DoesNotContain("d20", result.Formula, StringComparison.OrdinalIgnoreCase);
                break;

            case DiceConventionType.Percentile:
                // Percentile should use d100
                Assert.Contains("d100", result.Formula, StringComparison.OrdinalIgnoreCase);
                // Should NOT default to d20
                Assert.DoesNotContain("d20", result.Formula, StringComparison.OrdinalIgnoreCase);
                break;

            case DiceConventionType.SingleDieModifier:
                // d20 systems use d20+modifier notation
                Assert.Contains("d20", result.Formula, StringComparison.OrdinalIgnoreCase);
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetDieForType(DiceConventionType type)
    {
        return type switch
        {
            DiceConventionType.SingleDieModifier => "d20",
            DiceConventionType.DicePoolSuccess => "d6",
            DiceConventionType.FixedDiceThreshold => "d6",
            DiceConventionType.Fudge => "dF",
            DiceConventionType.StepDice => "d8",
            DiceConventionType.Percentile => "d100",
            _ => "d6"
        };
    }

    private static string GetExpectedConventionTypeString(DiceConventionType type)
    {
        return type switch
        {
            DiceConventionType.SingleDieModifier => "Single Die",
            DiceConventionType.DicePoolSuccess => "Dice Pool",
            DiceConventionType.FixedDiceThreshold => "Threshold",
            DiceConventionType.Fudge => "Fudge",
            DiceConventionType.StepDice => "Step Dice",
            DiceConventionType.Percentile => "Percentile",
            _ => type.ToString()
        };
    }
}
