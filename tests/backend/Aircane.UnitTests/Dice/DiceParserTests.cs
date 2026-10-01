using Aircane.Application.Dice;
using Xunit;

namespace Aircane.UnitTests.Dice;

/// <summary>
/// Unit tests for <see cref="DiceParser"/>.
/// </summary>
public class DiceParserTests
{
    // ── Standard expressions ──────────────────────────────────────────────────

    [Fact]
    public void Parse_d20_Returns1d20WithZeroModifier()
    {
        var expr = DiceParser.Parse("d20");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
        Assert.Equal(0, expr.Modifier);
        Assert.Null(expr.KeepHighest);
        Assert.Null(expr.KeepLowest);
        Assert.False(expr.IsAdvantage);
        Assert.False(expr.IsDisadvantage);
    }

    [Fact]
    public void Parse_1d20_Returns1d20WithZeroModifier()
    {
        var expr = DiceParser.Parse("1d20");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
        Assert.Equal(0, expr.Modifier);
    }

    [Fact]
    public void Parse_1d20Plus5_Returns1d20WithModifier5()
    {
        var expr = DiceParser.Parse("1d20+5");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
        Assert.Equal(5, expr.Modifier);
    }

    [Fact]
    public void Parse_2d6Plus3_Returns2d6WithModifier3()
    {
        var expr = DiceParser.Parse("2d6+3");

        Assert.Equal(2, expr.DiceCount);
        Assert.Equal(6, expr.DieSides);
        Assert.Equal(3, expr.Modifier);
    }

    [Fact]
    public void Parse_1d20Minus2_Returns1d20WithNegativeModifier()
    {
        var expr = DiceParser.Parse("1d20-2");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
        Assert.Equal(-2, expr.Modifier);
    }

    [Fact]
    public void Parse_1d8_Returns1d8WithZeroModifier()
    {
        var expr = DiceParser.Parse("1d8");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(8, expr.DieSides);
        Assert.Equal(0, expr.Modifier);
    }

    // ── Keep highest / lowest ─────────────────────────────────────────────────

    [Fact]
    public void Parse_4d6kh3_Returns4d6KeepHighest3()
    {
        var expr = DiceParser.Parse("4d6kh3");

        Assert.Equal(4, expr.DiceCount);
        Assert.Equal(6, expr.DieSides);
        Assert.Equal(3, expr.KeepHighest);
        Assert.Null(expr.KeepLowest);
        Assert.Equal(0, expr.Modifier);
    }

    [Fact]
    public void Parse_4d6kl3_Returns4d6KeepLowest3()
    {
        var expr = DiceParser.Parse("4d6kl3");

        Assert.Equal(4, expr.DiceCount);
        Assert.Equal(6, expr.DieSides);
        Assert.Equal(3, expr.KeepLowest);
        Assert.Null(expr.KeepHighest);
        Assert.Equal(0, expr.Modifier);
    }

    [Fact]
    public void Parse_4d6kh1_Returns4d6KeepHighest1()
    {
        var expr = DiceParser.Parse("4d6kh1");

        Assert.Equal(4, expr.DiceCount);
        Assert.Equal(6, expr.DieSides);
        Assert.Equal(1, expr.KeepHighest);
    }

    // ── Advantage / disadvantage shorthands ───────────────────────────────────

    [Fact]
    public void Parse_Advantage_Returns2d20KeepHighest1()
    {
        var expr = DiceParser.Parse("advantage");

        Assert.Equal(2, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
        Assert.Equal(1, expr.KeepHighest);
        Assert.Null(expr.KeepLowest);
        Assert.Equal(0, expr.Modifier);
        Assert.True(expr.IsAdvantage);
        Assert.False(expr.IsDisadvantage);
    }

    [Fact]
    public void Parse_Disadvantage_Returns2d20KeepLowest1()
    {
        var expr = DiceParser.Parse("disadvantage");

        Assert.Equal(2, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
        Assert.Equal(1, expr.KeepLowest);
        Assert.Null(expr.KeepHighest);
        Assert.Equal(0, expr.Modifier);
        Assert.False(expr.IsAdvantage);
        Assert.True(expr.IsDisadvantage);
    }

    // ── Case insensitivity ────────────────────────────────────────────────────

    [Theory]
    [InlineData("D20", 1, 20, 0)]
    [InlineData("1D20", 1, 20, 0)]
    [InlineData("1D20+5", 1, 20, 5)]
    [InlineData("2D6+3", 2, 6, 3)]
    public void Parse_UpperCaseExpression_ParsesSuccessfully(
        string expression, int expectedCount, int expectedSides, int expectedModifier)
    {
        var expr = DiceParser.Parse(expression);

        Assert.NotNull(expr);
        Assert.Equal(expectedCount, expr.DiceCount);
        Assert.Equal(expectedSides, expr.DieSides);
        Assert.Equal(expectedModifier, expr.Modifier);
    }

    [Theory]
    [InlineData("ADVANTAGE")]
    [InlineData("Advantage")]
    [InlineData("DISADVANTAGE")]
    [InlineData("Disadvantage")]
    public void Parse_AdvantageDisadvantageAnyCase_ParsesSuccessfully(string expression)
    {
        var expr = DiceParser.Parse(expression);

        Assert.NotNull(expr);
        Assert.Equal(2, expr.DiceCount);
        Assert.Equal(20, expr.DieSides);
    }

    [Theory]
    [InlineData("4D6KH3")]
    [InlineData("4d6KH3")]
    [InlineData("4D6kh3")]
    public void Parse_KeepHighestAnyCase_ParsesSuccessfully(string expression)
    {
        var expr = DiceParser.Parse(expression);

        Assert.Equal(4, expr.DiceCount);
        Assert.Equal(6, expr.DieSides);
        Assert.Equal(3, expr.KeepHighest);
    }

    // ── Whitespace tolerance ──────────────────────────────────────────────────

    [Theory]
    [InlineData("  d20  ")]
    [InlineData(" 1d20+5 ")]
    [InlineData(" advantage ")]
    public void Parse_ExpressionWithLeadingTrailingWhitespace_ParsesSuccessfully(string expression)
    {
        var expr = DiceParser.Parse(expression);

        Assert.NotNull(expr);
    }

    // ── Boundary values ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_1d2_MinimumValidDieSides_ParsesSuccessfully()
    {
        var expr = DiceParser.Parse("1d2");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(2, expr.DieSides);
    }

    [Fact]
    public void Parse_1d1000_MaximumValidDieSides_ParsesSuccessfully()
    {
        var expr = DiceParser.Parse("1d1000");

        Assert.Equal(1, expr.DiceCount);
        Assert.Equal(1000, expr.DieSides);
    }

    [Fact]
    public void Parse_100d6_MaximumValidDiceCount_ParsesSuccessfully()
    {
        var expr = DiceParser.Parse("100d6");

        Assert.Equal(100, expr.DiceCount);
        Assert.Equal(6, expr.DieSides);
    }

    [Fact]
    public void Parse_1d20Plus999_MaximumPositiveModifier_ParsesSuccessfully()
    {
        var expr = DiceParser.Parse("1d20+999");

        Assert.Equal(999, expr.Modifier);
    }

    [Fact]
    public void Parse_1d20Minus999_MaximumNegativeModifier_ParsesSuccessfully()
    {
        var expr = DiceParser.Parse("1d20-999");

        Assert.Equal(-999, expr.Modifier);
    }

    // ── Invalid expressions ───────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_NullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse(null!));
    }

    [Fact]
    public void Parse_WhitespaceOnly_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("   "));
    }

    [Fact]
    public void Parse_ArbitraryText_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("abc"));
    }

    [Fact]
    public void Parse_5d0_ThrowsArgumentException_DieSidesBelowMinimum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("5d0"));
    }

    [Fact]
    public void Parse_5d1_ThrowsArgumentException_DieSidesBelowMinimum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("5d1"));
    }

    [Fact]
    public void Parse_0d6_ThrowsArgumentException_DiceCountBelowMinimum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("0d6"));
    }

    [Fact]
    public void Parse_101d6_ThrowsArgumentException_DiceCountAboveMaximum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("101d6"));
    }

    [Fact]
    public void Parse_1d1001_ThrowsArgumentException_DieSidesAboveMaximum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("1d1001"));
    }

    [Fact]
    public void Parse_1d20Plus1000_ThrowsArgumentException_ModifierAboveMaximum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("1d20+1000"));
    }

    [Fact]
    public void Parse_1d20Minus1000_ThrowsArgumentException_ModifierBelowMinimum()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("1d20-1000"));
    }

    [Fact]
    public void Parse_4d6kh5_ThrowsArgumentException_KeepHighestExceedsDiceCount()
    {
        // Keep 5 from only 4 dice is invalid
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("4d6kh5"));
    }

    [Fact]
    public void Parse_4d6kl0_ThrowsArgumentException_KeepLowestIsZero()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("4d6kl0"));
    }

    [Fact]
    public void Parse_4d6kh0_ThrowsArgumentException_KeepHighestIsZero()
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse("4d6kh0"));
    }

    [Theory]
    [InlineData("d")]
    [InlineData("6")]
    [InlineData("d+5")]
    [InlineData("2d")]
    [InlineData("2d6+")]
    [InlineData("2d6-")]
    public void Parse_MalformedExpression_ThrowsArgumentException(string expression)
    {
        Assert.Throws<ArgumentException>(() => DiceParser.Parse(expression));
    }

    // ── TryParse ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_ValidExpression_ReturnsTrueAndResult()
    {
        var success = DiceParser.TryParse("2d6+3", out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(2, result!.DiceCount);
        Assert.Equal(6, result.DieSides);
        Assert.Equal(3, result.Modifier);
    }

    [Fact]
    public void TryParse_InvalidExpression_ReturnsFalseAndNullResult()
    {
        var success = DiceParser.TryParse("abc", out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalseAndNullResult()
    {
        var success = DiceParser.TryParse(string.Empty, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_Advantage_ReturnsTrueWithCorrectExpression()
    {
        var success = DiceParser.TryParse("advantage", out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.True(result!.IsAdvantage);
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_StandardExpression_ReturnsNormalisedForm()
    {
        var expr = DiceParser.Parse("1d20+5");

        Assert.Equal("1d20+5", expr.ToString());
    }

    [Fact]
    public void ToString_NegativeModifier_IncludesMinusSign()
    {
        var expr = DiceParser.Parse("1d20-3");

        Assert.Equal("1d20-3", expr.ToString());
    }

    [Fact]
    public void ToString_ZeroModifier_OmitsModifier()
    {
        var expr = DiceParser.Parse("2d6");

        Assert.Equal("2d6", expr.ToString());
    }

    [Fact]
    public void ToString_KeepHighest_IncludesKhSuffix()
    {
        var expr = DiceParser.Parse("4d6kh3");

        Assert.Equal("4d6kh3", expr.ToString());
    }

    [Fact]
    public void ToString_Advantage_ReturnsAdvantageString()
    {
        var expr = DiceParser.Parse("advantage");

        Assert.Equal("advantage", expr.ToString());
    }

    [Fact]
    public void ToString_Disadvantage_ReturnsDisadvantageString()
    {
        var expr = DiceParser.Parse("disadvantage");

        Assert.Equal("disadvantage", expr.ToString());
    }
}
