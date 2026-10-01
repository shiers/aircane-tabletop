using Aircane.Application.GameSystems;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Deterministic random source for testing. Returns values from a predefined sequence.
/// </summary>
public class DeterministicRandomSource : IRandomSource
{
    private readonly Queue<int> _values;

    public DeterministicRandomSource(params int[] values)
    {
        _values = new Queue<int>(values);
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (_values.Count == 0)
            throw new InvalidOperationException("DeterministicRandomSource ran out of values");
        return _values.Dequeue();
    }
}

public class MechanicResolverTests
{
    private readonly MechanicResolver _resolver = new();

    private static DiceConvention StandardConvention => new()
    {
        Type = DiceConventionType.SingleDieModifier,
        Name = "primary",
        Die = "d20"
    };

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

    private static DiceConvention FudgeConvention => new()
    {
        Type = DiceConventionType.Fudge,
        Name = "primary"
    };

    private static DiceConvention PercentileConvention => new()
    {
        Type = DiceConventionType.Percentile,
        Name = "primary",
        Die = "d100"
    };

    private static DiceConvention ExplodingConvention => new()
    {
        Type = DiceConventionType.SingleDieModifier,
        Name = "primary",
        Die = "d6"
    };

    #region ParseExpression Tests

    [Fact]
    public void ParseExpression_ValidStandard_ReturnsSuccess()
    {
        var result = _resolver.ParseExpression("2d6+3", StandardConvention);

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(2, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Equal(3, expr.Modifier);
    }

    [Fact]
    public void ParseExpression_InvalidExpression_ReturnsError()
    {
        var result = _resolver.ParseExpression("xyz", StandardConvention);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    #endregion

    #region Standard Roll Tests (d20 systems)

    [Fact]
    public void Roll_StandardDie_ReturnsCorrectTotal()
    {
        // Roll 1d20+5, die shows 14
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = 5 };
        var random = new DeterministicRandomSource(14);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(new[] { 14 }, result.RawResults);
        Assert.Equal(new[] { 14 }, result.KeptResults);
        Assert.Equal(5, result.Modifier);
        Assert.Equal(19, result.Total);
        Assert.Null(result.SuccessCount);
        Assert.Null(result.OutcomeTier);
    }

    [Fact]
    public void Roll_MultipleDice_SumsCorrectly()
    {
        // Roll 3d6+2, dice show 3, 5, 4
        var expr = new StandardDiceExpression { Count = 3, Sides = 6, Modifier = 2 };
        var random = new DeterministicRandomSource(3, 5, 4);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(new[] { 3, 5, 4 }, result.RawResults);
        Assert.Equal(new[] { 3, 5, 4 }, result.KeptResults);
        Assert.Equal(2, result.Modifier);
        Assert.Equal(14, result.Total); // 3+5+4+2
    }

    [Fact]
    public void Roll_NegativeModifier_SubtractsCorrectly()
    {
        // Roll 1d20-2, die shows 10
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = -2 };
        var random = new DeterministicRandomSource(10);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(8, result.Total); // 10-2
        Assert.Equal(-2, result.Modifier);
    }

    #endregion

    #region Keep/Drop Tests

    [Fact]
    public void Roll_KeepHighest_KeepsCorrectDice()
    {
        // Roll 4d6kh3, dice show 2, 5, 3, 6
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.KeepHighest, Amount = 3 }
        };
        var random = new DeterministicRandomSource(2, 5, 3, 6);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(new[] { 2, 5, 3, 6 }, result.RawResults);
        Assert.Equal(new[] { 6, 5, 3 }, result.KeptResults);
        Assert.Equal(14, result.Total); // 6+5+3
    }

    [Fact]
    public void Roll_KeepLowest_KeepsCorrectDice()
    {
        // Roll 4d6kl1, dice show 2, 5, 3, 6
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.KeepLowest, Amount = 1 }
        };
        var random = new DeterministicRandomSource(2, 5, 3, 6);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(new[] { 2, 5, 3, 6 }, result.RawResults);
        Assert.Equal(new[] { 2 }, result.KeptResults);
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public void Roll_DropHighest_DropsCorrectDice()
    {
        // Roll 4d6dh1, dice show 2, 5, 3, 6
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.DropHighest, Amount = 1 }
        };
        var random = new DeterministicRandomSource(2, 5, 3, 6);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(new[] { 2, 5, 3, 6 }, result.RawResults);
        Assert.Equal(new[] { 5, 3, 2 }, result.KeptResults);
        Assert.Equal(10, result.Total); // 5+3+2
    }

    [Fact]
    public void Roll_DropLowest_DropsCorrectDice()
    {
        // Roll 4d6dl1, dice show 2, 5, 3, 6
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.DropLowest, Amount = 1 }
        };
        var random = new DeterministicRandomSource(2, 5, 3, 6);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(new[] { 2, 5, 3, 6 }, result.RawResults);
        Assert.Equal(new[] { 3, 5, 6 }, result.KeptResults);
        Assert.Equal(14, result.Total); // 3+5+6
    }

    #endregion

    #region Dice Pool Tests (Shadowrun, WoD)

    [Fact]
    public void Roll_Pool_CountsSuccessesCorrectly()
    {
        // Roll 6d6>=5, dice show 1, 3, 5, 6, 2, 5
        var expr = new PoolDiceExpression { Count = 6, Sides = 6, SuccessThreshold = 5, Comparison = ">=" };
        var random = new DeterministicRandomSource(1, 3, 5, 6, 2, 5);

        var result = _resolver.Roll(expr, PoolConvention, random);

        Assert.Equal(new[] { 1, 3, 5, 6, 2, 5 }, result.RawResults);
        Assert.Equal(3, result.SuccessCount); // 5, 6, 5
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public void Roll_Pool_StrictGreaterThan_CountsCorrectly()
    {
        // Roll 4d10>7, dice show 7, 8, 10, 3
        var expr = new PoolDiceExpression { Count = 4, Sides = 10, SuccessThreshold = 7, Comparison = ">" };
        var random = new DeterministicRandomSource(7, 8, 10, 3);

        var result = _resolver.Roll(expr, PoolConvention, random);

        Assert.Equal(new[] { 7, 8, 10, 3 }, result.RawResults);
        Assert.Equal(2, result.SuccessCount); // 8, 10 (7 doesn't count with >)
    }

    [Fact]
    public void Roll_Pool_NoSuccesses_ReturnsZero()
    {
        // Roll 3d6>=5, dice show 1, 2, 4
        var expr = new PoolDiceExpression { Count = 3, Sides = 6, SuccessThreshold = 5, Comparison = ">=" };
        var random = new DeterministicRandomSource(1, 2, 4);

        var result = _resolver.Roll(expr, PoolConvention, random);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Roll_Pool_AllSuccesses_CountsAll()
    {
        // Roll 3d6>=3, dice show 4, 5, 6
        var expr = new PoolDiceExpression { Count = 3, Sides = 6, SuccessThreshold = 3, Comparison = ">=" };
        var random = new DeterministicRandomSource(4, 5, 6);

        var result = _resolver.Roll(expr, PoolConvention, random);

        Assert.Equal(3, result.SuccessCount);
    }

    #endregion

    #region Threshold Band Tests (PbtA)

    [Fact]
    public void Roll_ThresholdBand_Miss_WhenTotalIs6OrLess()
    {
        // Roll 2d6+0, dice show 2, 3 = total 5
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 0 };
        var random = new DeterministicRandomSource(2, 3);

        var result = _resolver.Roll(expr, ThresholdConvention, random);

        Assert.Equal(5, result.Total);
        Assert.Equal("Miss", result.OutcomeTier);
    }

    [Fact]
    public void Roll_ThresholdBand_WeakHit_WhenTotalIs7To9()
    {
        // Roll 2d6+1, dice show 3, 4 = total 8
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 1 };
        var random = new DeterministicRandomSource(3, 4);

        var result = _resolver.Roll(expr, ThresholdConvention, random);

        Assert.Equal(8, result.Total);
        Assert.Equal("Weak Hit", result.OutcomeTier);
    }

    [Fact]
    public void Roll_ThresholdBand_StrongHit_WhenTotalIs10OrMore()
    {
        // Roll 2d6+2, dice show 5, 4 = total 11
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 2 };
        var random = new DeterministicRandomSource(5, 4);

        var result = _resolver.Roll(expr, ThresholdConvention, random);

        Assert.Equal(11, result.Total);
        Assert.Equal("Strong Hit", result.OutcomeTier);
    }

    [Fact]
    public void Roll_ThresholdBand_BoundaryAt6_IsMiss()
    {
        // Roll 2d6+0, dice show 2, 4 = total 6
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 0 };
        var random = new DeterministicRandomSource(2, 4);

        var result = _resolver.Roll(expr, ThresholdConvention, random);

        Assert.Equal(6, result.Total);
        Assert.Equal("Miss", result.OutcomeTier);
    }

    [Fact]
    public void Roll_ThresholdBand_BoundaryAt7_IsWeakHit()
    {
        // Roll 2d6+0, dice show 3, 4 = total 7
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 0 };
        var random = new DeterministicRandomSource(3, 4);

        var result = _resolver.Roll(expr, ThresholdConvention, random);

        Assert.Equal(7, result.Total);
        Assert.Equal("Weak Hit", result.OutcomeTier);
    }

    [Fact]
    public void Roll_ThresholdBand_BoundaryAt10_IsStrongHit()
    {
        // Roll 2d6+0, dice show 5, 5 = total 10
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 0 };
        var random = new DeterministicRandomSource(5, 5);

        var result = _resolver.Roll(expr, ThresholdConvention, random);

        Assert.Equal(10, result.Total);
        Assert.Equal("Strong Hit", result.OutcomeTier);
    }

    #endregion

    #region Fudge Dice Tests

    [Fact]
    public void Roll_Fudge_ProducesCorrectValues()
    {
        // Roll 4dF+2, random produces 0,1,2,0 which maps to -1,0,+1,-1
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 2 };
        // random.Next(0, 3) - 1: 0->-1, 1->0, 2->+1
        var random = new DeterministicRandomSource(0, 1, 2, 0);

        var result = _resolver.Roll(expr, FudgeConvention, random);

        Assert.Equal(new[] { -1, 0, 1, -1 }, result.RawResults);
        Assert.Equal(2, result.Modifier);
        Assert.Equal(1, result.Total); // (-1+0+1-1)+2 = 1
    }

    [Fact]
    public void Roll_Fudge_AllPositive()
    {
        // Roll 4dF+0, all dice show +1
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 0 };
        var random = new DeterministicRandomSource(2, 2, 2, 2);

        var result = _resolver.Roll(expr, FudgeConvention, random);

        Assert.Equal(new[] { 1, 1, 1, 1 }, result.RawResults);
        Assert.Equal(4, result.Total);
    }

    [Fact]
    public void Roll_Fudge_AllNegative()
    {
        // Roll 4dF+0, all dice show -1
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 0 };
        var random = new DeterministicRandomSource(0, 0, 0, 0);

        var result = _resolver.Roll(expr, FudgeConvention, random);

        Assert.Equal(new[] { -1, -1, -1, -1 }, result.RawResults);
        Assert.Equal(-4, result.Total);
    }

    [Fact]
    public void Roll_Fudge_AllZero()
    {
        // Roll 4dF+3, all dice show 0
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 3 };
        var random = new DeterministicRandomSource(1, 1, 1, 1);

        var result = _resolver.Roll(expr, FudgeConvention, random);

        Assert.Equal(new[] { 0, 0, 0, 0 }, result.RawResults);
        Assert.Equal(3, result.Total); // 0+3
    }

    #endregion

    #region Step Dice Tests (Savage Worlds)

    [Fact]
    public void Roll_StepDice_VariableDieSize()
    {
        // Step dice is just a standard roll with a variable die size
        // e.g., d4 for low trait, d12 for high trait
        var expr = new StandardDiceExpression { Count = 1, Sides = 8, Modifier = 0 };
        var random = new DeterministicRandomSource(6);

        var convention = new DiceConvention
        {
            Type = DiceConventionType.StepDice,
            Name = "primary",
            StepDiceLadder = new[] { "d4", "d6", "d8", "d10", "d12" }
        };

        var result = _resolver.Roll(expr, convention, random);

        Assert.Equal(new[] { 6 }, result.RawResults);
        Assert.Equal(6, result.Total);
    }

    #endregion

    #region Percentile Tests (d100)

    [Fact]
    public void Roll_Percentile_ReturnsD100Result()
    {
        // Roll 1d100, die shows 73
        var expr = new StandardDiceExpression { Count = 1, Sides = 100, Modifier = 0 };
        var random = new DeterministicRandomSource(73);

        var result = _resolver.Roll(expr, PercentileConvention, random);

        Assert.Equal(new[] { 73 }, result.RawResults);
        Assert.Equal(73, result.Total);
    }

    [Fact]
    public void Roll_Percentile_WithModifier()
    {
        // Roll 1d100+10, die shows 45
        var expr = new StandardDiceExpression { Count = 1, Sides = 100, Modifier = 10 };
        var random = new DeterministicRandomSource(45);

        var result = _resolver.Roll(expr, PercentileConvention, random);

        Assert.Equal(55, result.Total); // 45+10
    }

    #endregion

    #region Exploding Dice Tests

    [Fact]
    public void Roll_Exploding_NoExplosion()
    {
        // Roll 2d6!, dice show 3, 4 (no explosions since neither is 6)
        var expr = new ExplodingDiceExpression { Count = 2, Sides = 6, ExplodeThreshold = null };
        var random = new DeterministicRandomSource(3, 4);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        Assert.Equal(new[] { 3, 4 }, result.RawResults);
        Assert.Equal(7, result.Total);
        Assert.Null(result.ExplodedResults);
    }

    [Fact]
    public void Roll_Exploding_SingleExplosion()
    {
        // Roll 2d6!, dice show 6, 3. The 6 explodes, re-roll shows 4.
        var expr = new ExplodingDiceExpression { Count = 2, Sides = 6, ExplodeThreshold = null };
        var random = new DeterministicRandomSource(6, 3, 4);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        Assert.Equal(new[] { 6, 3 }, result.RawResults);
        Assert.Equal(new[] { 4 }, result.ExplodedResults);
        Assert.Equal(13, result.Total); // 6+3+4
    }

    [Fact]
    public void Roll_Exploding_ChainExplosion()
    {
        // Roll 1d6!, die shows 6, re-roll shows 6, re-roll shows 3
        var expr = new ExplodingDiceExpression { Count = 1, Sides = 6, ExplodeThreshold = null };
        var random = new DeterministicRandomSource(6, 6, 3);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        Assert.Equal(new[] { 6 }, result.RawResults);
        Assert.Equal(new[] { 6, 3 }, result.ExplodedResults);
        Assert.Equal(15, result.Total); // 6+6+3
    }

    [Fact]
    public void Roll_Exploding_CustomThreshold()
    {
        // Roll 2d6!>4, dice show 5, 2. The 5 explodes (>=4), re-roll shows 3.
        var expr = new ExplodingDiceExpression { Count = 2, Sides = 6, ExplodeThreshold = 4 };
        var random = new DeterministicRandomSource(5, 2, 3);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        Assert.Equal(new[] { 5, 2 }, result.RawResults);
        Assert.Equal(new[] { 3 }, result.ExplodedResults);
        Assert.Equal(10, result.Total); // 5+2+3
    }

    [Fact]
    public void Roll_Exploding_UsesConventionThreshold_WhenExpressionHasNone()
    {
        // Convention has ExplodeThreshold = 5, expression has no explicit threshold
        var convention = new DiceConvention
        {
            Type = DiceConventionType.SingleDieModifier,
            Name = "primary",
            Die = "d6",
            ExplodeThreshold = 5
        };
        // Roll 2d6!, dice show 5, 2. The 5 explodes (>=5 from convention), re-roll shows 3.
        var expr = new ExplodingDiceExpression { Count = 2, Sides = 6, ExplodeThreshold = null };
        var random = new DeterministicRandomSource(5, 2, 3);

        var result = _resolver.Roll(expr, convention, random);

        Assert.Equal(new[] { 5, 2 }, result.RawResults);
        Assert.Equal(new[] { 3 }, result.ExplodedResults);
        Assert.Equal(10, result.Total);
    }

    [Fact]
    public void Roll_Exploding_MultipleExplosions()
    {
        // Roll 3d6!, dice show 6, 6, 2. Both 6s explode, re-rolls show 4, 6. The second re-roll (6) explodes again, shows 2.
        var expr = new ExplodingDiceExpression { Count = 3, Sides = 6, ExplodeThreshold = null };
        var random = new DeterministicRandomSource(6, 6, 2, 4, 6, 2);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        Assert.Equal(new[] { 6, 6, 2 }, result.RawResults);
        Assert.Equal(new[] { 4, 6, 2 }, result.ExplodedResults);
        Assert.Equal(26, result.Total); // 6+6+2+4+6+2
    }

    #endregion

    #region RollResolution Completeness Tests

    [Fact]
    public void Roll_Standard_ReturnsNonEmptyRawResults()
    {
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 1 };
        var random = new DeterministicRandomSource(3, 4);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.NotEmpty(result.RawResults);
        Assert.NotEmpty(result.KeptResults);
    }

    [Fact]
    public void Roll_Pool_ReturnsNonEmptyRawResults()
    {
        var expr = new PoolDiceExpression { Count = 3, Sides = 6, SuccessThreshold = 5, Comparison = ">=" };
        var random = new DeterministicRandomSource(1, 5, 6);

        var result = _resolver.Roll(expr, PoolConvention, random);

        Assert.NotEmpty(result.RawResults);
        Assert.NotNull(result.SuccessCount);
    }

    [Fact]
    public void Roll_Fudge_ReturnsNonEmptyRawResults()
    {
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 0 };
        var random = new DeterministicRandomSource(0, 1, 2, 1);

        var result = _resolver.Roll(expr, FudgeConvention, random);

        Assert.NotEmpty(result.RawResults);
        Assert.Equal(4, result.RawResults.Count);
    }

    [Fact]
    public void Roll_Exploding_ReturnsNonEmptyRawResults()
    {
        var expr = new ExplodingDiceExpression { Count = 2, Sides = 6, ExplodeThreshold = null };
        var random = new DeterministicRandomSource(3, 4);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        Assert.NotEmpty(result.RawResults);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Roll_Standard_SingleDie_NoModifier()
    {
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = 0 };
        var random = new DeterministicRandomSource(17);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Equal(17, result.Total);
        Assert.Equal(0, result.Modifier);
    }

    [Fact]
    public void Roll_Standard_NoThresholdBand_WhenNotPbtA()
    {
        // Standard convention should NOT produce outcome tiers
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = 0 };
        var random = new DeterministicRandomSource(5);

        var result = _resolver.Roll(expr, StandardConvention, random);

        Assert.Null(result.OutcomeTier);
    }

    [Fact]
    public void Roll_Exploding_SafetyLimit_PreventsInfiniteLoop()
    {
        // Create a scenario where every roll would explode (all max values)
        // The safety limit of 100 should prevent infinite recursion
        var expr = new ExplodingDiceExpression { Count = 1, Sides = 6, ExplodeThreshold = null };

        // Provide 102 values: initial roll of 6, then 100 re-rolls of 6, then a final 3 (won't be reached)
        var values = new int[102];
        for (var i = 0; i < 101; i++) values[i] = 6;
        values[101] = 3;
        var random = new DeterministicRandomSource(values);

        var result = _resolver.Roll(expr, ExplodingConvention, random);

        // Should have initial roll + at most 100 exploded results
        Assert.Single(result.RawResults);
        Assert.NotNull(result.ExplodedResults);
        Assert.True(result.ExplodedResults!.Count <= 100);
    }

    #endregion
}
