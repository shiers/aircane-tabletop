using Aircane.Application.GameSystems;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// A deterministic random source that returns values from a pre-generated list in order.
/// Used for property-based testing of the MechanicResolver.
/// </summary>
public class SequentialRandomSource : IRandomSource
{
    private readonly int[] _values;
    private int _index;

    public SequentialRandomSource(int[] values)
    {
        _values = values;
        _index = 0;
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (_index >= _values.Length)
            throw new InvalidOperationException("SequentialRandomSource ran out of values");

        var value = _values[_index++];

        // Clamp to the requested range to ensure validity
        if (value < minInclusive) value = minInclusive;
        if (value >= maxExclusive) value = maxExclusive - 1;

        return value;
    }

    /// <summary>Number of values consumed so far.</summary>
    public int Consumed => _index;
}

/// <summary>
/// Property-based tests for the MechanicResolver dice rolling engine.
/// Tests Properties 5-9 from the design document.
/// </summary>
public class MechanicResolverPropertyTests
{
    private static readonly MechanicResolver Resolver = new();

    // ── Shared Conventions ────────────────────────────────────────────────────

    private static DiceConvention PoolConvention => new()
    {
        Type = DiceConventionType.DicePoolSuccess,
        Name = "primary",
        Die = "d6",
        SuccessThreshold = 5
    };

    private static DiceConvention ThresholdConvention => new()
    {
        Type = DiceConventionType.FixedDiceThreshold,
        Name = "primary",
        Die = "d6"
    };

    private static DiceConvention ExplodingConvention => new()
    {
        Type = DiceConventionType.SingleDieModifier,
        Name = "primary",
        Die = "d6"
    };

    private static DiceConvention FudgeConvention => new()
    {
        Type = DiceConventionType.Fudge,
        Name = "primary"
    };

    private static DiceConvention StandardConvention => new()
    {
        Type = DiceConventionType.SingleDieModifier,
        Name = "primary",
        Die = "d20"
    };

    // ══════════════════════════════════════════════════════════════════════════
    // Property 5: Dice pool success counting invariant
    // **Validates: Requirements 2.2**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 5: dice pool success counting.
    /// </summary>
    public record PoolTestInput(int Count, int Sides, int Threshold, string Comparison, int[] DieValues);

    /// <summary>
    /// Generates valid PoolTestInput: N dice (1-20), X sides (2-20), threshold T (1-X),
    /// comparison (">=" or ">"), and N random values each in [1, X].
    /// </summary>
    private static Gen<PoolTestInput> PoolTestInputGen =>
        from count in Gen.Choose(1, 20)
        from sides in Gen.Choose(2, 20)
        from threshold in Gen.Choose(1, sides)
        from comparison in Gen.Elements(">=", ">")
        from dieValues in Gen.ArrayOf(count, Gen.Choose(1, sides))
        select new PoolTestInput(count, sides, threshold, comparison, dieValues);

    public static Arbitrary<PoolTestInput> PoolTestInputArbitrary =>
        Arb.From(PoolTestInputGen);

    /// <summary>
    /// Property 5: For any dice pool roll with N dice of X sides and success threshold T,
    /// the reported success count SHALL equal the number of individual die results in the
    /// raw results array that are >= T (for ">=") or > T (for ">").
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MechanicResolverPropertyTests) }, MaxTest = 300)]
    public void DicePool_SuccessCount_EqualsCountOfResultsMeetingThreshold(PoolTestInput input)
    {
        var expr = new PoolDiceExpression
        {
            Count = input.Count,
            Sides = input.Sides,
            SuccessThreshold = input.Threshold,
            Comparison = input.Comparison
        };

        var random = new SequentialRandomSource(input.DieValues);
        var result = Resolver.Roll(expr, PoolConvention, random);

        // Independently compute expected success count from raw results
        var expectedSuccessCount = input.Comparison switch
        {
            ">=" => result.RawResults.Count(r => r >= input.Threshold),
            ">" => result.RawResults.Count(r => r > input.Threshold),
            _ => throw new InvalidOperationException($"Unknown comparison: {input.Comparison}")
        };

        Assert.Equal(expectedSuccessCount, result.SuccessCount);
        Assert.Equal(input.Count, result.RawResults.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 6: Threshold band mapping produces exactly one outcome tier
    // **Validates: Requirements 2.3**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 6: threshold band mapping.
    /// Uses 2d6+M where M varies to produce different totals.
    /// </summary>
    public record ThresholdTestInput(int Modifier, int Die1, int Die2);

    /// <summary>
    /// Generates valid ThresholdTestInput: modifier (-10 to +10), two d6 values.
    /// </summary>
    private static Gen<ThresholdTestInput> ThresholdTestInputGen =>
        from modifier in Gen.Choose(-10, 10)
        from die1 in Gen.Choose(1, 6)
        from die2 in Gen.Choose(1, 6)
        select new ThresholdTestInput(modifier, die1, die2);

    public static Arbitrary<ThresholdTestInput> ThresholdTestInputArbitrary =>
        Arb.From(ThresholdTestInputGen);

    /// <summary>
    /// Property 6: For any roll total and PbtA convention, the resolver SHALL map the total
    /// to exactly one outcome tier. Total <= 6 → "Miss", 7-9 → "Weak Hit", 10+ → "Strong Hit".
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MechanicResolverPropertyTests) }, MaxTest = 300)]
    public void ThresholdBand_MapsToExactlyOneOutcomeTier(ThresholdTestInput input)
    {
        var expr = new StandardDiceExpression
        {
            Count = 2,
            Sides = 6,
            Modifier = input.Modifier
        };

        var random = new SequentialRandomSource(new[] { input.Die1, input.Die2 });
        var result = Resolver.Roll(expr, ThresholdConvention, random);

        // OutcomeTier must be non-null
        Assert.NotNull(result.OutcomeTier);

        // Must be exactly one of the valid tiers
        var validTiers = new[] { "Miss", "Weak Hit", "Strong Hit" };
        Assert.Contains(result.OutcomeTier, validTiers);

        // Verify correct mapping based on total
        var expectedTotal = input.Die1 + input.Die2 + input.Modifier;
        Assert.Equal(expectedTotal, result.Total);

        var expectedTier = expectedTotal switch
        {
            <= 6 => "Miss",
            <= 9 => "Weak Hit",
            _ => "Strong Hit"
        };

        Assert.Equal(expectedTier, result.OutcomeTier);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 7: Exploding dice structural correctness
    // **Validates: Requirements 2.4**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 7: exploding dice.
    /// </summary>
    public record ExplodingTestInput(int Count, int Sides, int? ExplodeThreshold, int[] AllValues);

    /// <summary>
    /// Generates valid ExplodingTestInput: N dice (1-10), X sides (2-10),
    /// threshold (null for max, or 1-X), and enough random values to cover
    /// initial rolls plus potential explosions.
    /// The generator ensures the sequence terminates by making the last few values
    /// below the explosion threshold.
    /// </summary>
    private static Gen<ExplodingTestInput> ExplodingTestInputGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 10)
        from hasThreshold in Gen.Elements(true, false)
        from threshold in Gen.Choose(2, sides) // threshold >= 2 so there's always a non-exploding value
        from initialValues in Gen.ArrayOf(count, Gen.Choose(1, sides))
        // Generate some exploding re-roll values, then terminate with non-exploding values
        from explodingCount in Gen.Choose(0, 10)
        from explodingValues in Gen.ArrayOf(explodingCount, Gen.Choose(1, sides))
        // Ensure we have enough terminating values (below threshold) to stop all chains
        let effectiveThreshold = hasThreshold ? threshold : sides
        from terminatingValues in Gen.ArrayOf(count + explodingCount + 10,
            Gen.Choose(1, Math.Max(1, effectiveThreshold - 1)))
        let allValues = initialValues.Concat(explodingValues).Concat(terminatingValues).ToArray()
        select new ExplodingTestInput(count, sides, hasThreshold ? threshold : (int?)null, allValues);

    public static Arbitrary<ExplodingTestInput> ExplodingTestInputArbitrary =>
        Arb.From(ExplodingTestInputGen);

    /// <summary>
    /// Property 7: For any exploding dice roll where K dice in the initial roll meet the
    /// explosion threshold, the raw results array SHALL contain the initial N dice, and
    /// ExplodedResults SHALL contain at least K additional results (from re-rolls) when
    /// any initial die meets the threshold.
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MechanicResolverPropertyTests) }, MaxTest = 300)]
    public void ExplodingDice_StructuralCorrectness_ExplodedResultsMatchExplosions(ExplodingTestInput input)
    {
        var expr = new ExplodingDiceExpression
        {
            Count = input.Count,
            Sides = input.Sides,
            ExplodeThreshold = input.ExplodeThreshold
        };

        var random = new SequentialRandomSource(input.AllValues);
        var result = Resolver.Roll(expr, ExplodingConvention, random);

        // The effective threshold: explicit or max value of die
        var effectiveThreshold = input.ExplodeThreshold ?? input.Sides;

        // RawResults must contain exactly N initial dice
        Assert.Equal(input.Count, result.RawResults.Count);

        // Count how many initial dice meet the explosion threshold
        var initialExplosions = result.RawResults.Count(r => r >= effectiveThreshold);

        if (initialExplosions > 0)
        {
            // ExplodedResults must be non-null and contain at least as many entries
            // as initial dice that exploded
            Assert.NotNull(result.ExplodedResults);
            Assert.True(result.ExplodedResults!.Count >= initialExplosions,
                $"Expected at least {initialExplosions} exploded results, got {result.ExplodedResults.Count}. " +
                $"Initial rolls: [{string.Join(", ", result.RawResults)}], threshold: {effectiveThreshold}");
        }
        else
        {
            // No explosions occurred - ExplodedResults should be null
            Assert.Null(result.ExplodedResults);
        }

        // All raw results must be in valid die range [1, Sides]
        foreach (var r in result.RawResults)
        {
            Assert.InRange(r, 1, input.Sides);
        }

        if (result.ExplodedResults is not null)
        {
            foreach (var r in result.ExplodedResults)
            {
                Assert.InRange(r, 1, input.Sides);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 8: Fudge dice value range and total invariant
    // **Validates: Requirements 2.5**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 8: Fudge dice.
    /// </summary>
    public record FudgeTestInput(int Count, int Modifier, int[] RawRandomValues);

    /// <summary>
    /// Generates valid FudgeTestInput: N dice (1-20), modifier (-20 to +20),
    /// and N random values in [0, 2] (which map to -1, 0, +1).
    /// </summary>
    private static Gen<FudgeTestInput> FudgeTestInputGen =>
        from count in Gen.Choose(1, 20)
        from modifier in Gen.Choose(-20, 20)
        from rawValues in Gen.ArrayOf(count, Gen.Choose(0, 2))
        select new FudgeTestInput(count, modifier, rawValues);

    public static Arbitrary<FudgeTestInput> FudgeTestInputArbitrary =>
        Arb.From(FudgeTestInputGen);

    /// <summary>
    /// Property 8: For any Fudge dice roll of N dice with modifier M, every individual die
    /// result SHALL be in {-1, 0, +1}, and the total SHALL equal sum of all die results plus M.
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MechanicResolverPropertyTests) }, MaxTest = 300)]
    public void FudgeDice_AllResultsInRange_AndTotalEqualsSum(FudgeTestInput input)
    {
        var expr = new FudgeDiceExpression
        {
            Count = input.Count,
            Modifier = input.Modifier
        };

        var random = new SequentialRandomSource(input.RawRandomValues);
        var result = Resolver.Roll(expr, FudgeConvention, random);

        // All raw results must be in {-1, 0, +1}
        Assert.Equal(input.Count, result.RawResults.Count);
        foreach (var r in result.RawResults)
        {
            Assert.InRange(r, -1, 1);
        }

        // Total must equal sum of raw results + modifier
        var expectedTotal = result.RawResults.Sum() + input.Modifier;
        Assert.Equal(expectedTotal, result.Total);

        // Modifier must be correctly reported
        Assert.Equal(input.Modifier, result.Modifier);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 9: Roll resolution completeness
    // **Validates: Requirements 2.1, 2.6**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for Property 9: roll resolution completeness.
    /// Wraps a DiceExpressionNode with its required random values and convention.
    /// </summary>
    public record CompletenessTestInput(
        DiceExpressionNode Expression,
        DiceConvention Convention,
        int[] RandomValues,
        string ExpressionType);

    /// <summary>
    /// Generates a standard dice completeness test input.
    /// </summary>
    private static Gen<CompletenessTestInput> StandardCompletenessGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 20)
        from modifier in Gen.Choose(-10, 10)
        from values in Gen.ArrayOf(count, Gen.Choose(1, sides))
        select new CompletenessTestInput(
            new StandardDiceExpression { Count = count, Sides = sides, Modifier = modifier },
            new DiceConvention { Type = DiceConventionType.SingleDieModifier, Name = "primary", Die = "d20" },
            values,
            "Standard");

    /// <summary>
    /// Generates a pool dice completeness test input.
    /// </summary>
    private static Gen<CompletenessTestInput> PoolCompletenessGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 20)
        from threshold in Gen.Choose(1, sides)
        from comparison in Gen.Elements(">=", ">")
        from values in Gen.ArrayOf(count, Gen.Choose(1, sides))
        select new CompletenessTestInput(
            new PoolDiceExpression { Count = count, Sides = sides, SuccessThreshold = threshold, Comparison = comparison },
            new DiceConvention { Type = DiceConventionType.DicePoolSuccess, Name = "primary", Die = "d6", SuccessThreshold = threshold },
            values,
            "Pool");

    /// <summary>
    /// Generates an exploding dice completeness test input.
    /// Values are chosen to mostly NOT explode to keep things simple.
    /// </summary>
    private static Gen<CompletenessTestInput> ExplodingCompletenessGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(3, 10)
        // Use values that won't trigger explosions (below max)
        from values in Gen.ArrayOf(count, Gen.Choose(1, sides - 1))
        select new CompletenessTestInput(
            new ExplodingDiceExpression { Count = count, Sides = sides, ExplodeThreshold = null },
            new DiceConvention { Type = DiceConventionType.SingleDieModifier, Name = "primary", Die = "d6" },
            values,
            "Exploding");

    /// <summary>
    /// Generates a Fudge dice completeness test input.
    /// </summary>
    private static Gen<CompletenessTestInput> FudgeCompletenessGen =>
        from count in Gen.Choose(1, 10)
        from modifier in Gen.Choose(-10, 10)
        from values in Gen.ArrayOf(count, Gen.Choose(0, 2))
        select new CompletenessTestInput(
            new FudgeDiceExpression { Count = count, Modifier = modifier },
            new DiceConvention { Type = DiceConventionType.Fudge, Name = "primary" },
            values,
            "Fudge");

    /// <summary>
    /// Generates a threshold band completeness test input.
    /// </summary>
    private static Gen<CompletenessTestInput> ThresholdCompletenessGen =>
        from modifier in Gen.Choose(-5, 5)
        from die1 in Gen.Choose(1, 6)
        from die2 in Gen.Choose(1, 6)
        select new CompletenessTestInput(
            new StandardDiceExpression { Count = 2, Sides = 6, Modifier = modifier },
            new DiceConvention { Type = DiceConventionType.FixedDiceThreshold, Name = "primary", Die = "d6" },
            new[] { die1, die2 },
            "Threshold");

    /// <summary>
    /// Generates any valid completeness test input.
    /// </summary>
    private static Gen<CompletenessTestInput> AnyCompletenessGen =>
        Gen.OneOf(
            StandardCompletenessGen,
            PoolCompletenessGen,
            ExplodingCompletenessGen,
            FudgeCompletenessGen,
            ThresholdCompletenessGen);

    public static Arbitrary<CompletenessTestInput> CompletenessTestInputArbitrary =>
        Arb.From(AnyCompletenessGen);

    /// <summary>
    /// Property 9: For any valid dice expression and convention, the resolver SHALL return
    /// a result containing: raw dice results (non-empty), applied modifiers, a final total
    /// or success count, and an outcome tier when the resolution rule defines one.
    /// **Validates: Requirements 2.1, 2.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MechanicResolverPropertyTests) }, MaxTest = 500)]
    public void RollResolution_IsComplete_ForAnyValidExpression(CompletenessTestInput input)
    {
        var random = new SequentialRandomSource(input.RandomValues);
        var result = Resolver.Roll(input.Expression, input.Convention, random);

        // RawResults must be non-empty for all expression types
        Assert.NotEmpty(result.RawResults);

        // For pool expressions, SuccessCount must be non-null
        if (input.ExpressionType == "Pool")
        {
            Assert.NotNull(result.SuccessCount);
            Assert.True(result.SuccessCount >= 0,
                "SuccessCount must be non-negative for pool expressions");
        }

        // For threshold band conventions, OutcomeTier must be non-null
        if (input.ExpressionType == "Threshold")
        {
            Assert.NotNull(result.OutcomeTier);
            var validTiers = new[] { "Miss", "Weak Hit", "Strong Hit" };
            Assert.Contains(result.OutcomeTier, validTiers);
        }

        // KeptResults must be non-empty
        Assert.NotEmpty(result.KeptResults);

        // Total must be set (it's always an int, so just verify it's computed)
        // For pool expressions, Total equals SuccessCount
        if (input.ExpressionType == "Pool")
        {
            Assert.Equal(result.SuccessCount, result.Total);
        }
    }
}
