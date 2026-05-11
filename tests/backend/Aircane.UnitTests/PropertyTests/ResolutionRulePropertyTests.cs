using Aircane.Application.GameSystems;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for the MechanicResolver resolution rule engine.
/// Tests Properties 10-11 from the design document.
/// </summary>
public class ResolutionRulePropertyTests
{
    private static readonly MechanicResolver Resolver = new();

    // ══════════════════════════════════════════════════════════════════════════
    // Property 10: Resolution rule produces exactly one outcome
    // **Validates: Requirements 3.1, 3.2**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 10: target number resolution.
    /// </summary>
    public record TargetNumberTestInput(int RollTotal, int TargetValue, string Comparison);

    /// <summary>
    /// Generates valid TargetNumberTestInput: roll total (1-40), target (1-30), comparison operator.
    /// </summary>
    private static Gen<TargetNumberTestInput> TargetNumberTestInputGen =>
        from rollTotal in Gen.Choose(1, 40)
        from target in Gen.Choose(1, 30)
        from comparison in Gen.Elements(">=", ">", "<=", "<")
        select new TargetNumberTestInput(rollTotal, target, comparison);

    public static Arbitrary<TargetNumberTestInput> TargetNumberTestInputArbitrary =>
        Arb.From(TargetNumberTestInputGen);

    /// <summary>
    /// Property 10 (Target Number): For any target number resolution rule and valid roll result,
    /// the resolver SHALL produce exactly one non-null, non-empty outcome classification with no error.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ResolutionRulePropertyTests) }, MaxTest = 300)]
    public void TargetNumber_ProducesExactlyOneOutcome(TargetNumberTestInput input)
    {
        var roll = new RollResolution
        {
            RawResults = new[] { input.RollTotal },
            KeptResults = new[] { input.RollTotal },
            Modifier = 0,
            Total = input.RollTotal
        };

        var rule = new ResolutionRule
        {
            Name = "test_check",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = input.Comparison,
            TargetSource = "dc"
        };

        var characterState = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = input.TargetValue }
        };

        var outcome = Resolver.Resolve(roll, rule, characterState);

        // Outcome must be non-null and non-empty
        Assert.NotNull(outcome);
        Assert.False(string.IsNullOrEmpty(outcome.Outcome));

        // Error must be null for valid inputs
        Assert.Null(outcome.Error);

        // Outcome must be exactly one of the valid classifications
        var validOutcomes = new[] { "Success", "Failure" };
        Assert.Contains(outcome.Outcome, validOutcomes);
    }

    /// <summary>
    /// Test input for Property 10: degrees of success resolution.
    /// </summary>
    public record DegreesOfSuccessTestInput(int RollTotal, int ThresholdCount);

    /// <summary>
    /// Generates valid DegreesOfSuccessTestInput: roll total (-5 to 35), threshold count (2-5).
    /// </summary>
    private static Gen<DegreesOfSuccessTestInput> DegreesOfSuccessTestInputGen =>
        from rollTotal in Gen.Choose(-5, 35)
        from thresholdCount in Gen.Choose(2, 5)
        select new DegreesOfSuccessTestInput(rollTotal, thresholdCount);

    public static Arbitrary<DegreesOfSuccessTestInput> DegreesOfSuccessTestInputArbitrary =>
        Arb.From(DegreesOfSuccessTestInputGen);

    /// <summary>
    /// Property 10 (Degrees of Success): When degrees of success are defined with non-overlapping
    /// thresholds covering the full range, the roll SHALL match exactly one degree threshold.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ResolutionRulePropertyTests) }, MaxTest = 300)]
    public void DegreesOfSuccess_MatchesExactlyOneThreshold(DegreesOfSuccessTestInput input)
    {
        // Build non-overlapping thresholds that cover the full integer range
        // Example for 4 thresholds: CritFail (null-5), Fail (6-14), Success (15-24), CritSuccess (25-null)
        var thresholds = BuildNonOverlappingThresholds(input.ThresholdCount);

        var roll = new RollResolution
        {
            RawResults = new[] { input.RollTotal },
            KeptResults = new[] { input.RollTotal },
            Modifier = 0,
            Total = input.RollTotal
        };

        var rule = new ResolutionRule
        {
            Name = "test_degrees",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess = thresholds
        };

        var outcome = Resolver.Resolve(roll, rule);

        // Outcome must be non-null and non-empty
        Assert.NotNull(outcome);
        Assert.False(string.IsNullOrEmpty(outcome.Outcome));

        // Error must be null for valid inputs
        Assert.Null(outcome.Error);

        // The outcome must match exactly one of the defined threshold names
        var thresholdNames = thresholds.Select(t => t.Name).ToList();
        Assert.Contains(outcome.Outcome, thresholdNames);

        // Verify it matches exactly one threshold (count matching thresholds)
        var matchingThresholds = thresholds.Where(t =>
        {
            var meetsMin = t.MinValue is null || input.RollTotal >= t.MinValue;
            var meetsMax = t.MaxValue is null || input.RollTotal <= t.MaxValue;
            return meetsMin && meetsMax;
        }).ToList();

        Assert.Single(matchingThresholds);
        Assert.Equal(matchingThresholds[0].Name, outcome.Outcome);
    }

    /// <summary>
    /// Test input for Property 10: margin resolution.
    /// </summary>
    public record MarginTestInput(int RollTotal, int TargetValue);

    /// <summary>
    /// Generates valid MarginTestInput: roll total (1-40), target (1-30).
    /// </summary>
    private static Gen<MarginTestInput> MarginTestInputGen =>
        from rollTotal in Gen.Choose(1, 40)
        from target in Gen.Choose(1, 30)
        select new MarginTestInput(rollTotal, target);

    public static Arbitrary<MarginTestInput> MarginTestInputArbitrary =>
        Arb.From(MarginTestInputGen);

    /// <summary>
    /// Property 10 (Margin): For any margin resolution rule and valid roll result,
    /// the resolver SHALL produce exactly one outcome with a non-null margin value.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ResolutionRulePropertyTests) }, MaxTest = 300)]
    public void Margin_ProducesExactlyOneOutcomeWithMargin(MarginTestInput input)
    {
        var roll = new RollResolution
        {
            RawResults = new[] { input.RollTotal },
            KeptResults = new[] { input.RollTotal },
            Modifier = 0,
            Total = input.RollTotal
        };

        var rule = new ResolutionRule
        {
            Name = "test_margin",
            Type = ResolutionRuleType.Margin,
            Roll = "primary",
            TargetSource = "dc"
        };

        var characterState = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = input.TargetValue }
        };

        var outcome = Resolver.Resolve(roll, rule, characterState);

        // Outcome must be non-null and non-empty
        Assert.NotNull(outcome);
        Assert.False(string.IsNullOrEmpty(outcome.Outcome));

        // Error must be null for valid inputs
        Assert.Null(outcome.Error);

        // Outcome must be exactly one of the valid classifications
        var validOutcomes = new[] { "Success", "Failure" };
        Assert.Contains(outcome.Outcome, validOutcomes);

        // Margin must be non-null and equal to roll - target
        Assert.NotNull(outcome.Margin);
        Assert.Equal(input.RollTotal - input.TargetValue, outcome.Margin);
    }

    /// <summary>
    /// Test input for Property 10: threshold bands resolution.
    /// </summary>
    public record ThresholdBandsTestInput(int RollTotal);

    /// <summary>
    /// Generates valid ThresholdBandsTestInput: roll total (-5 to 20).
    /// </summary>
    private static Gen<ThresholdBandsTestInput> ThresholdBandsTestInputGen =>
        from rollTotal in Gen.Choose(-5, 20)
        select new ThresholdBandsTestInput(rollTotal);

    public static Arbitrary<ThresholdBandsTestInput> ThresholdBandsTestInputArbitrary =>
        Arb.From(ThresholdBandsTestInputGen);

    /// <summary>
    /// Property 10 (Threshold Bands): For any threshold band resolution rule and valid roll result,
    /// the resolver SHALL produce exactly one outcome tier.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ResolutionRulePropertyTests) }, MaxTest = 300)]
    public void ThresholdBands_ProducesExactlyOneOutcomeTier(ThresholdBandsTestInput input)
    {
        var roll = new RollResolution
        {
            RawResults = new[] { input.RollTotal },
            KeptResults = new[] { input.RollTotal },
            Modifier = 0,
            Total = input.RollTotal,
            OutcomeTier = input.RollTotal switch
            {
                <= 6 => "Miss",
                <= 9 => "Weak Hit",
                _ => "Strong Hit"
            }
        };

        var rule = new ResolutionRule
        {
            Name = "test_threshold",
            Type = ResolutionRuleType.ThresholdBands,
            Roll = "primary"
        };

        var outcome = Resolver.Resolve(roll, rule);

        // Outcome must be non-null and non-empty
        Assert.NotNull(outcome);
        Assert.False(string.IsNullOrEmpty(outcome.Outcome));

        // Error must be null for valid inputs
        Assert.Null(outcome.Error);

        // Outcome must be exactly one of the valid tiers
        var validTiers = new[] { "Miss", "Weak Hit", "Strong Hit" };
        Assert.Contains(outcome.Outcome, validTiers);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 11: Opposed roll resolution consistency
    // **Validates: Requirements 3.3**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 11: opposed roll resolution.
    /// </summary>
    public record OpposedRollTestInput(int AttackerTotal, int DefenderTotal, string TieBreaker);

    /// <summary>
    /// Generates valid OpposedRollTestInput: attacker total (1-40), defender total (1-40),
    /// tie-breaker ("attacker_wins", "defender_wins", "reroll").
    /// </summary>
    private static Gen<OpposedRollTestInput> OpposedRollTestInputGen =>
        from attackerTotal in Gen.Choose(1, 40)
        from defenderTotal in Gen.Choose(1, 40)
        from tieBreaker in Gen.Elements("attacker_wins", "defender_wins", "reroll")
        select new OpposedRollTestInput(attackerTotal, defenderTotal, tieBreaker);

    public static Arbitrary<OpposedRollTestInput> OpposedRollTestInputArbitrary =>
        Arb.From(OpposedRollTestInputGen);

    /// <summary>
    /// Property 11: For any two roll results and an opposed resolution rule with a tie-breaking
    /// specification, the resolver SHALL produce a deterministic winner/loser/tie outcome
    /// consistent with the tie-breaking rule.
    /// When attacker > defender → "Attacker Wins"
    /// When defender > attacker → "Defender Wins"
    /// When equal and tie-breaker is "attacker_wins" → "Attacker Wins"
    /// When equal and tie-breaker is "defender_wins" → "Defender Wins"
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ResolutionRulePropertyTests) }, MaxTest = 300)]
    public void OpposedRoll_ProducesDeterministicOutcome_ConsistentWithTieBreaker(OpposedRollTestInput input)
    {
        var attackerRoll = new RollResolution
        {
            RawResults = new[] { input.AttackerTotal },
            KeptResults = new[] { input.AttackerTotal },
            Modifier = 0,
            Total = input.AttackerTotal
        };

        var rule = new ResolutionRule
        {
            Name = "test_opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = input.TieBreaker
        };

        var characterState = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = input.DefenderTotal }
        };

        var outcome = Resolver.Resolve(attackerRoll, rule, characterState);

        // Outcome must be non-null and non-empty
        Assert.NotNull(outcome);
        Assert.False(string.IsNullOrEmpty(outcome.Outcome));

        // Error must be null for valid inputs
        Assert.Null(outcome.Error);

        // Verify deterministic outcome based on totals and tie-breaker
        if (input.AttackerTotal > input.DefenderTotal)
        {
            Assert.Equal("Attacker Wins", outcome.Outcome);
            Assert.True(outcome.IsSuccess);
        }
        else if (input.DefenderTotal > input.AttackerTotal)
        {
            Assert.Equal("Defender Wins", outcome.Outcome);
            Assert.False(outcome.IsSuccess);
        }
        else // Tied
        {
            var expectedOutcome = input.TieBreaker switch
            {
                "attacker_wins" => "Attacker Wins",
                "defender_wins" => "Defender Wins",
                _ => "Tie"
            };
            Assert.Equal(expectedOutcome, outcome.Outcome);
        }
    }

    /// <summary>
    /// Property 11 (Swap): Swapping attacker and defender totals SHALL swap the outcome
    /// (when not tied).
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ResolutionRulePropertyTests) }, MaxTest = 300)]
    public void OpposedRoll_SwappingTotals_SwapsOutcome_WhenNotTied(OpposedRollTestInput input)
    {
        // Skip tied cases — swapping tied values doesn't change anything
        if (input.AttackerTotal == input.DefenderTotal)
            return;

        var rule = new ResolutionRule
        {
            Name = "test_opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = input.TieBreaker
        };

        // Original: attacker has AttackerTotal, defender has DefenderTotal
        var originalRoll = new RollResolution
        {
            RawResults = new[] { input.AttackerTotal },
            KeptResults = new[] { input.AttackerTotal },
            Modifier = 0,
            Total = input.AttackerTotal
        };

        var originalState = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = input.DefenderTotal }
        };

        var originalOutcome = Resolver.Resolve(originalRoll, rule, originalState);

        // Swapped: attacker has DefenderTotal, defender has AttackerTotal
        var swappedRoll = new RollResolution
        {
            RawResults = new[] { input.DefenderTotal },
            KeptResults = new[] { input.DefenderTotal },
            Modifier = 0,
            Total = input.DefenderTotal
        };

        var swappedState = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = input.AttackerTotal }
        };

        var swappedOutcome = Resolver.Resolve(swappedRoll, rule, swappedState);

        // Swapping totals should swap the outcome
        if (originalOutcome.Outcome == "Attacker Wins")
        {
            Assert.Equal("Defender Wins", swappedOutcome.Outcome);
        }
        else if (originalOutcome.Outcome == "Defender Wins")
        {
            Assert.Equal("Attacker Wins", swappedOutcome.Outcome);
        }
    }

    // ── Helper Methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds non-overlapping degree thresholds that cover the full integer range.
    /// For N thresholds, creates bands like: (-inf, 5], [6, 14], [15, 24], [25, +inf)
    /// </summary>
    private static List<DegreeThreshold> BuildNonOverlappingThresholds(int count)
    {
        var names = new[] { "Critical Failure", "Failure", "Success", "Critical Success", "Legendary Success" };
        var thresholds = new List<DegreeThreshold>();

        // Create evenly spaced bands starting from 0
        var bandWidth = 10;

        for (var i = 0; i < count; i++)
        {
            int? minValue = i == 0 ? null : i * bandWidth;
            int? maxValue = i == count - 1 ? null : (i + 1) * bandWidth - 1;

            thresholds.Add(new DegreeThreshold
            {
                Name = names[i],
                MinValue = minValue,
                MaxValue = maxValue
            });
        }

        return thresholds;
    }
}
