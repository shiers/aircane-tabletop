using Aircane.Domain.DiceExpressions;
using Xunit;

namespace Aircane.UnitTests.Dice;

/// <summary>
/// Unit tests for <see cref="DiceExpressionPrinter"/>.
/// Covers all notation types: standard, pool, exploding, and Fudge.
/// </summary>
public class DiceExpressionPrinterTests
{
    // ── Standard notation ─────────────────────────────────────────────────────

    [Fact]
    public void Print_StandardWithPositiveModifier_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression { Count = 2, Sides = 6, Modifier = 3 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("2d6+3", result);
    }

    [Fact]
    public void Print_StandardWithZeroModifier_OmitsModifier()
    {
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = 0 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("1d20", result);
    }

    [Fact]
    public void Print_StandardWithNegativeModifier_FormatsWithMinus()
    {
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = -2 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("1d20-2", result);
    }

    [Fact]
    public void Print_StandardWithKeepHighest_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.KeepHighest, Amount = 3 }
        };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4d6kh3", result);
    }

    [Fact]
    public void Print_StandardWithKeepLowest_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression
        {
            Count = 2,
            Sides = 20,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.KeepLowest, Amount = 1 }
        };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("2d20kl1", result);
    }

    [Fact]
    public void Print_StandardWithDropHighest_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.DropHighest, Amount = 1 }
        };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4d6dh1", result);
    }

    [Fact]
    public void Print_StandardWithDropLowest_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 0,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.DropLowest, Amount = 1 }
        };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4d6dl1", result);
    }

    [Fact]
    public void Print_StandardWithKeepDropAndModifier_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = 2,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.KeepHighest, Amount = 3 }
        };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4d6kh3+2", result);
    }

    [Fact]
    public void Print_StandardWithKeepDropAndNegativeModifier_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression
        {
            Count = 4,
            Sides = 6,
            Modifier = -1,
            KeepDrop = new KeepDropDirective { Type = KeepDropType.KeepHighest, Amount = 3 }
        };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4d6kh3-1", result);
    }

    // ── Pool notation ─────────────────────────────────────────────────────────

    [Fact]
    public void Print_PoolWithGreaterThanOrEqual_FormatsCorrectly()
    {
        var expr = new PoolDiceExpression { Count = 6, Sides = 6, SuccessThreshold = 5, Comparison = ">=" };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("6d6>=5", result);
    }

    [Fact]
    public void Print_PoolWithGreaterThan_FormatsCorrectly()
    {
        var expr = new PoolDiceExpression { Count = 10, Sides = 10, SuccessThreshold = 7, Comparison = ">" };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("10d10>7", result);
    }

    // ── Exploding notation ────────────────────────────────────────────────────

    [Fact]
    public void Print_ExplodingWithoutThreshold_FormatsCorrectly()
    {
        var expr = new ExplodingDiceExpression { Count = 2, Sides = 6, ExplodeThreshold = null };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("2d6!", result);
    }

    [Fact]
    public void Print_ExplodingWithThreshold_FormatsCorrectly()
    {
        var expr = new ExplodingDiceExpression { Count = 3, Sides = 10, ExplodeThreshold = 8 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("3d10!>8", result);
    }

    // ── Fudge notation ────────────────────────────────────────────────────────

    [Fact]
    public void Print_FudgeWithPositiveModifier_FormatsCorrectly()
    {
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 2 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4dF+2", result);
    }

    [Fact]
    public void Print_FudgeWithZeroModifier_OmitsModifier()
    {
        var expr = new FudgeDiceExpression { Count = 4, Modifier = 0 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4dF", result);
    }

    [Fact]
    public void Print_FudgeWithNegativeModifier_FormatsWithMinus()
    {
        var expr = new FudgeDiceExpression { Count = 4, Modifier = -1 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("4dF-1", result);
    }

    // ── Null handling ─────────────────────────────────────────────────────────

    [Fact]
    public void Print_NullNode_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DiceExpressionPrinter.Print(null!));
    }

    // ── Round-trip examples ───────────────────────────────────────────────────

    [Theory]
    [InlineData("2d6+3")]
    [InlineData("1d20")]
    [InlineData("1d20-2")]
    [InlineData("4d6kh3")]
    [InlineData("2d20kl1")]
    [InlineData("4d6dh1")]
    [InlineData("4d6dl1")]
    [InlineData("4d6kh3+2")]
    [InlineData("6d6>=5")]
    [InlineData("10d10>7")]
    [InlineData("2d6!")]
    [InlineData("3d10!>8")]
    [InlineData("4dF+2")]
    [InlineData("4dF")]
    [InlineData("4dF-1")]
    public void Print_ParseThenPrint_ProducesCanonicalNotation(string canonical)
    {
        var parseResult = DiceExpressionParser.Parse(canonical);
        Assert.True(parseResult.IsSuccess, $"Failed to parse '{canonical}'");

        var printed = DiceExpressionPrinter.Print(parseResult.Expression!);

        Assert.Equal(canonical, printed);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Print_LargeDiceCount_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression { Count = 100, Sides = 6, Modifier = 0 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("100d6", result);
    }

    [Fact]
    public void Print_LargeModifier_FormatsCorrectly()
    {
        var expr = new StandardDiceExpression { Count = 1, Sides = 20, Modifier = 15 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("1d20+15", result);
    }

    [Fact]
    public void Print_SingleFudgeDie_FormatsCorrectly()
    {
        var expr = new FudgeDiceExpression { Count = 1, Modifier = 0 };

        var result = DiceExpressionPrinter.Print(expr);

        Assert.Equal("1dF", result);
    }
}
