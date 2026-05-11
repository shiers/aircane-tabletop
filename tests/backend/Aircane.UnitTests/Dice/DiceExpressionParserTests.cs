using Aircane.Domain.DiceExpressions;
using Xunit;

namespace Aircane.UnitTests.Dice;

/// <summary>
/// Unit tests for <see cref="DiceExpressionParser"/>.
/// Covers all notation types: standard, pool, exploding, and Fudge.
/// </summary>
public class DiceExpressionParserTests
{
    // ── Standard notation: NdX+M ──────────────────────────────────────────────

    [Fact]
    public void Parse_2d6Plus3_ReturnsStandardExpression()
    {
        var result = DiceExpressionParser.Parse("2d6+3");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(2, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Equal(3, expr.Modifier);
        Assert.Null(expr.KeepDrop);
    }

    [Fact]
    public void Parse_1d20_ReturnsStandardWithZeroModifier()
    {
        var result = DiceExpressionParser.Parse("1d20");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(1, expr.Count);
        Assert.Equal(20, expr.Sides);
        Assert.Equal(0, expr.Modifier);
        Assert.Null(expr.KeepDrop);
    }

    [Fact]
    public void Parse_d20_DefaultsCountTo1()
    {
        var result = DiceExpressionParser.Parse("d20");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(1, expr.Count);
        Assert.Equal(20, expr.Sides);
        Assert.Equal(0, expr.Modifier);
    }

    [Fact]
    public void Parse_1d20Minus2_ReturnsNegativeModifier()
    {
        var result = DiceExpressionParser.Parse("1d20-2");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(1, expr.Count);
        Assert.Equal(20, expr.Sides);
        Assert.Equal(-2, expr.Modifier);
    }

    [Fact]
    public void Parse_3d8Plus10_ReturnsCorrectValues()
    {
        var result = DiceExpressionParser.Parse("3d8+10");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(3, expr.Count);
        Assert.Equal(8, expr.Sides);
        Assert.Equal(10, expr.Modifier);
    }

    // ── Standard notation: case insensitivity ─────────────────────────────────

    [Theory]
    [InlineData("D20")]
    [InlineData("1D20")]
    [InlineData("2D6+3")]
    public void Parse_UpperCaseD_ParsesSuccessfully(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.True(result.IsSuccess);
        Assert.IsType<StandardDiceExpression>(result.Expression);
    }

    // ── Keep/drop directives ──────────────────────────────────────────────────

    [Fact]
    public void Parse_4d6kh3_ReturnsKeepHighest()
    {
        var result = DiceExpressionParser.Parse("4d6kh3");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Equal(0, expr.Modifier);
        Assert.NotNull(expr.KeepDrop);
        Assert.Equal(KeepDropType.KeepHighest, expr.KeepDrop!.Type);
        Assert.Equal(3, expr.KeepDrop.Amount);
    }

    [Fact]
    public void Parse_2d20kl1_ReturnsKeepLowest()
    {
        var result = DiceExpressionParser.Parse("2d20kl1");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(2, expr.Count);
        Assert.Equal(20, expr.Sides);
        Assert.NotNull(expr.KeepDrop);
        Assert.Equal(KeepDropType.KeepLowest, expr.KeepDrop!.Type);
        Assert.Equal(1, expr.KeepDrop.Amount);
    }

    [Fact]
    public void Parse_4d6dh1_ReturnsDropHighest()
    {
        var result = DiceExpressionParser.Parse("4d6dh1");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.NotNull(expr.KeepDrop);
        Assert.Equal(KeepDropType.DropHighest, expr.KeepDrop!.Type);
        Assert.Equal(1, expr.KeepDrop.Amount);
    }

    [Fact]
    public void Parse_4d6dl1_ReturnsDropLowest()
    {
        var result = DiceExpressionParser.Parse("4d6dl1");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.NotNull(expr.KeepDrop);
        Assert.Equal(KeepDropType.DropLowest, expr.KeepDrop!.Type);
        Assert.Equal(1, expr.KeepDrop.Amount);
    }

    [Theory]
    [InlineData("4D6KH3")]
    [InlineData("4d6KH3")]
    [InlineData("4D6kh3")]
    public void Parse_KeepHighestAnyCase_ParsesSuccessfully(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(KeepDropType.KeepHighest, expr.KeepDrop!.Type);
        Assert.Equal(3, expr.KeepDrop.Amount);
    }

    // ── Pool notation: NdX>=T, NdX>T ─────────────────────────────────────────

    [Fact]
    public void Parse_6d6GreaterThanOrEqual5_ReturnsPoolExpression()
    {
        var result = DiceExpressionParser.Parse("6d6>=5");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<PoolDiceExpression>(result.Expression);
        Assert.Equal(6, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Equal(5, expr.SuccessThreshold);
        Assert.Equal(">=", expr.Comparison);
    }

    [Fact]
    public void Parse_10d10GreaterThan7_ReturnsPoolExpression()
    {
        var result = DiceExpressionParser.Parse("10d10>7");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<PoolDiceExpression>(result.Expression);
        Assert.Equal(10, expr.Count);
        Assert.Equal(10, expr.Sides);
        Assert.Equal(7, expr.SuccessThreshold);
        Assert.Equal(">", expr.Comparison);
    }

    [Fact]
    public void Parse_5d6GreaterThanOrEqual4_ReturnsPoolExpression()
    {
        var result = DiceExpressionParser.Parse("5d6>=4");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<PoolDiceExpression>(result.Expression);
        Assert.Equal(5, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Equal(4, expr.SuccessThreshold);
        Assert.Equal(">=", expr.Comparison);
    }

    // ── Exploding notation: NdX!, NdX!>T ─────────────────────────────────────

    [Fact]
    public void Parse_2d6Exploding_ReturnsExplodingWithNullThreshold()
    {
        var result = DiceExpressionParser.Parse("2d6!");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<ExplodingDiceExpression>(result.Expression);
        Assert.Equal(2, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Null(expr.ExplodeThreshold);
    }

    [Fact]
    public void Parse_3d10ExplodingGreaterThan8_ReturnsExplodingWithThreshold()
    {
        var result = DiceExpressionParser.Parse("3d10!>8");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<ExplodingDiceExpression>(result.Expression);
        Assert.Equal(3, expr.Count);
        Assert.Equal(10, expr.Sides);
        Assert.Equal(8, expr.ExplodeThreshold);
    }

    [Fact]
    public void Parse_1d6Exploding_ReturnsExplodingExpression()
    {
        var result = DiceExpressionParser.Parse("1d6!");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<ExplodingDiceExpression>(result.Expression);
        Assert.Equal(1, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Null(expr.ExplodeThreshold);
    }

    // ── Fudge notation: NdF+M ─────────────────────────────────────────────────

    [Fact]
    public void Parse_4dFPlus2_ReturnsFudgeExpression()
    {
        var result = DiceExpressionParser.Parse("4dF+2");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<FudgeDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(2, expr.Modifier);
    }

    [Fact]
    public void Parse_4dF_ReturnsFudgeWithZeroModifier()
    {
        var result = DiceExpressionParser.Parse("4dF");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<FudgeDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(0, expr.Modifier);
    }

    [Fact]
    public void Parse_4dFMinus1_ReturnsFudgeWithNegativeModifier()
    {
        var result = DiceExpressionParser.Parse("4dF-1");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<FudgeDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(-1, expr.Modifier);
    }

    [Fact]
    public void Parse_dF_DefaultsCountTo1()
    {
        var result = DiceExpressionParser.Parse("dF");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<FudgeDiceExpression>(result.Expression);
        Assert.Equal(1, expr.Count);
        Assert.Equal(0, expr.Modifier);
    }

    [Theory]
    [InlineData("4df")]
    [InlineData("4DF")]
    [InlineData("4Df")]
    public void Parse_FudgeCaseInsensitive_ParsesSuccessfully(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.True(result.IsSuccess);
        Assert.IsType<FudgeDiceExpression>(result.Expression);
    }

    // ── Whitespace handling ───────────────────────────────────────────────────

    [Theory]
    [InlineData("  2d6+3  ")]
    [InlineData(" 4dF+2 ")]
    [InlineData("  6d6>=5  ")]
    [InlineData(" 2d6! ")]
    public void Parse_WithLeadingTrailingWhitespace_ParsesSuccessfully(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.True(result.IsSuccess);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.Error!.Position);
    }

    [Fact]
    public void Parse_Null_ReturnsError()
    {
        var result = DiceExpressionParser.Parse(null!);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("   ");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_ArbitraryText_ReturnsErrorAtPosition0()
    {
        var result = DiceExpressionParser.Parse("abc");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.Error!.Position);
        Assert.Contains("'d'", result.Error.Expected);
    }

    [Fact]
    public void Parse_JustNumber_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("6");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_JustD_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("d");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("die sides", result.Error!.Expected);
    }

    [Fact]
    public void Parse_2d_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("2d");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("die sides", result.Error!.Expected);
    }

    [Fact]
    public void Parse_2d6Plus_ReturnsErrorMissingModifierValue()
    {
        var result = DiceExpressionParser.Parse("2d6+");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("modifier value", result.Error!.Expected);
    }

    [Fact]
    public void Parse_2d6Minus_ReturnsErrorMissingModifierValue()
    {
        var result = DiceExpressionParser.Parse("2d6-");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("modifier value", result.Error!.Expected);
    }

    [Fact]
    public void Parse_PoolMissingThreshold_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("6d6>=");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("success threshold", result.Error!.Expected);
    }

    [Fact]
    public void Parse_ExplodingMissingThreshold_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("2d6!>");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("explode threshold", result.Error!.Expected);
    }

    [Fact]
    public void Parse_KeepDropAmountExceedsDiceCount_ReturnsError()
    {
        // Keep 4 from 4 dice is invalid (must keep fewer than total)
        var result = DiceExpressionParser.Parse("4d6kh4");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("keep/drop amount", result.Error!.Expected);
    }

    [Fact]
    public void Parse_KeepDropAmountZero_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("4d6kh0");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("keep/drop amount", result.Error!.Expected);
    }

    [Fact]
    public void Parse_KeepMissingType_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("4d6k3");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("'h'", result.Error!.Expected);
    }

    [Fact]
    public void Parse_TrailingGarbage_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("2d6+3abc");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("end of expression", result.Error!.Expected);
    }

    [Fact]
    public void Parse_FudgeTrailingGarbage_ReturnsError()
    {
        var result = DiceExpressionParser.Parse("4dF+2x");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    // ── Error position accuracy ───────────────────────────────────────────────

    [Fact]
    public void Parse_ErrorPosition_PointsToCorrectCharacter()
    {
        // "2d6+x" — error should be at position 4 (the 'x')
        var result = DiceExpressionParser.Parse("2d6+x");

        Assert.False(result.IsSuccess);
        Assert.Equal(4, result.Error!.Position);
    }

    [Fact]
    public void Parse_ErrorAtStart_PositionIsZero()
    {
        var result = DiceExpressionParser.Parse("xyz");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Error!.Position);
    }

    // ── Comprehensive examples from task description ──────────────────────────

    [Theory]
    [InlineData("2d6+3", typeof(StandardDiceExpression))]
    [InlineData("1d20", typeof(StandardDiceExpression))]
    [InlineData("d20", typeof(StandardDiceExpression))]
    [InlineData("4d6kh3", typeof(StandardDiceExpression))]
    [InlineData("2d20kl1", typeof(StandardDiceExpression))]
    [InlineData("6d6>=5", typeof(PoolDiceExpression))]
    [InlineData("10d10>7", typeof(PoolDiceExpression))]
    [InlineData("2d6!", typeof(ExplodingDiceExpression))]
    [InlineData("3d10!>8", typeof(ExplodingDiceExpression))]
    [InlineData("4dF+2", typeof(FudgeDiceExpression))]
    [InlineData("4dF", typeof(FudgeDiceExpression))]
    public void Parse_AllExamplesFromSpec_ParseSuccessfully(string expression, Type expectedType)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.True(result.IsSuccess, $"Failed to parse '{expression}': {result.Error?.Expected}");
        Assert.IsType(expectedType, result.Expression);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_LargeDiceCount_ParsesSuccessfully()
    {
        var result = DiceExpressionParser.Parse("100d6");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(100, expr.Count);
    }

    [Fact]
    public void Parse_LargeDieSides_ParsesSuccessfully()
    {
        var result = DiceExpressionParser.Parse("1d100");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(100, expr.Sides);
    }

    [Fact]
    public void Parse_PoolWithLargeThreshold_ParsesSuccessfully()
    {
        var result = DiceExpressionParser.Parse("8d10>=8");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<PoolDiceExpression>(result.Expression);
        Assert.Equal(8, expr.SuccessThreshold);
    }

    [Fact]
    public void Parse_ExplodingWithHighThreshold_ParsesSuccessfully()
    {
        var result = DiceExpressionParser.Parse("5d6!>5");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<ExplodingDiceExpression>(result.Expression);
        Assert.Equal(5, expr.ExplodeThreshold);
    }

    [Fact]
    public void Parse_StandardWithKeepDropAndModifier_NotSupported()
    {
        // Keep/drop followed by modifier — the parser should handle this
        // "4d6kh3+2" means roll 4d6, keep highest 3, then add 2
        var result = DiceExpressionParser.Parse("4d6kh3+2");

        Assert.True(result.IsSuccess);
        var expr = Assert.IsType<StandardDiceExpression>(result.Expression);
        Assert.Equal(4, expr.Count);
        Assert.Equal(6, expr.Sides);
        Assert.Equal(KeepDropType.KeepHighest, expr.KeepDrop!.Type);
        Assert.Equal(3, expr.KeepDrop.Amount);
        Assert.Equal(2, expr.Modifier);
    }
}
