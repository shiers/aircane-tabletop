using Aircane.Domain.DiceExpressions;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for invalid dice expression error reporting.
/// **Validates: Requirements 14.7**
///
/// Property 21: Invalid dice expression error reporting
/// For any syntactically invalid dice expression, the parser SHALL return an error result
/// (not throw) that includes the character position where parsing failed and a description
/// of what was expected.
/// </summary>
public class DiceExpressionErrorPropertyTests
{
    // ── Generators for invalid dice expressions ───────────────────────────────

    /// <summary>
    /// Generates pure alphabetic strings without 'd' character (never valid dice expressions).
    /// </summary>
    private static Gen<string> PureAlphabeticNoDGen =>
        from length in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(length, Gen.Elements(
            'a', 'b', 'c', 'e', 'f', 'g', 'h', 'i', 'j', 'l',
            'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v',
            'w', 'x', 'y', 'z'))
        select new string(chars);

    /// <summary>
    /// Generates strings starting with special characters (not digits or 'd').
    /// </summary>
    private static Gen<string> SpecialCharStartGen =>
        from startChar in Gen.Elements('@', '#', '$', '%', '&', '*', '(', ')', '[', ']', '{', '}', '|', '\\', '/', '~', '`')
        from rest in Gen.Elements("abc", "123", "d6", "stuff", "", "xyz")
        select $"{startChar}{rest}";

    /// <summary>
    /// Generates expressions with 'd' but missing sides (e.g., "2d", "d").
    /// </summary>
    private static Gen<string> MissingSidesGen =>
        from count in Gen.Choose(1, 20)
        from useCount in Gen.Elements(true, false)
        select useCount ? $"{count}d" : "d";

    /// <summary>
    /// Generates expressions with trailing garbage after a valid prefix (e.g., "2d6+3abc").
    /// </summary>
    private static Gen<string> TrailingGarbageGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 20)
        from modifier in Gen.Choose(1, 10)
        from garbage in Gen.Elements("abc", "xyz", "!!!", "??", "##", "@@", "dd", "++5")
        from prefix in Gen.Elements(
            $"{count}d{sides}+{modifier}{garbage}",
            $"{count}d{sides}{garbage}",
            $"{count}d{sides}-{modifier}{garbage}")
        select prefix;

    /// <summary>
    /// Generates empty or whitespace-only strings.
    /// </summary>
    private static Gen<string> EmptyOrWhitespaceGen =>
        Gen.Elements("", " ", "  ", "\t", "\n", "   \t  ");

    /// <summary>
    /// Generates expressions with invalid modifiers (trailing operator with no value).
    /// </summary>
    private static Gen<string> InvalidModifierGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 20)
        from op in Gen.Elements("+", "-")
        select $"{count}d{sides}{op}";

    /// <summary>
    /// Generates expressions with invalid pool notation (missing threshold).
    /// </summary>
    private static Gen<string> InvalidPoolGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 20)
        from suffix in Gen.Elements(">=", ">")
        select $"{count}d{sides}{suffix}";

    /// <summary>
    /// Generates expressions with invalid exploding notation (e.g., "2d6!>").
    /// </summary>
    private static Gen<string> InvalidExplodingGen =>
        from count in Gen.Choose(1, 10)
        from sides in Gen.Choose(2, 20)
        select $"{count}d{sides}!>";

    /// <summary>
    /// Generates expressions with invalid keep/drop (missing amount, e.g., "4d6k", "4d6kh").
    /// </summary>
    private static Gen<string> InvalidKeepDropGen =>
        from count in Gen.Choose(2, 10)
        from sides in Gen.Choose(2, 20)
        from suffix in Gen.Elements("k", "kh", "kl", "dh", "dl")
        select $"{count}d{sides}{suffix}";

    /// <summary>
    /// Generates completely random strings (most will be invalid).
    /// Filters out strings that happen to be valid dice expressions.
    /// </summary>
    private static Gen<string> RandomStringGen =>
        from length in Gen.Choose(1, 30)
        from chars in Gen.ArrayOf(length, Gen.Choose(32, 126).Select(i => (char)i))
        let s = new string(chars)
        where !IsValidDiceExpression(s)
        select s;

    /// <summary>
    /// Combined generator for all types of invalid expressions.
    /// </summary>
    private static Gen<string> InvalidDiceExpressionGen =>
        Gen.OneOf(
            PureAlphabeticNoDGen,
            SpecialCharStartGen,
            MissingSidesGen,
            TrailingGarbageGen,
            EmptyOrWhitespaceGen,
            InvalidModifierGen,
            InvalidPoolGen,
            InvalidExplodingGen,
            InvalidKeepDropGen,
            RandomStringGen);

    // ── Arbitraries ───────────────────────────────────────────────────────────

    public static Arbitrary<string> InvalidExpressionArbitrary =>
        Arb.From(InvalidDiceExpressionGen);

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks if a string is a valid dice expression by attempting to parse it.
    /// Used to filter random strings that accidentally produce valid expressions.
    /// </summary>
    private static bool IsValidDiceExpression(string input)
    {
        try
        {
            var result = DiceExpressionParser.Parse(input);
            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 21a: For any invalid dice expression, the parser returns IsSuccess == false
    /// (does not throw an exception).
    /// **Validates: Requirements 14.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DiceExpressionErrorPropertyTests) }, MaxTest = 500)]
    public void InvalidExpression_ReturnsFailure_DoesNotThrow(string expression)
    {
        // Act - should never throw
        var exception = Record.Exception(() => DiceExpressionParser.Parse(expression));
        Assert.Null(exception);

        var result = DiceExpressionParser.Parse(expression);

        // Assert - must be a failure
        Assert.False(result.IsSuccess,
            $"Expected failure for invalid expression '{expression}' but got success");
    }

    /// <summary>
    /// Property 21b: For any invalid dice expression, the error has Position >= 0.
    /// **Validates: Requirements 14.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DiceExpressionErrorPropertyTests) }, MaxTest = 500)]
    public void InvalidExpression_ErrorHasNonNegativePosition(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.False(result.IsSuccess,
            $"Expected failure for invalid expression '{expression}' but got success");
        Assert.NotNull(result.Error);
        Assert.True(result.Error!.Position >= 0,
            $"Error position should be >= 0 but was {result.Error.Position} for expression '{expression}'");
    }

    /// <summary>
    /// Property 21c: For any invalid dice expression, the error has a non-empty Expected string.
    /// **Validates: Requirements 14.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DiceExpressionErrorPropertyTests) }, MaxTest = 500)]
    public void InvalidExpression_ErrorHasNonEmptyExpected(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.False(result.IsSuccess,
            $"Expected failure for invalid expression '{expression}' but got success");
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Expected),
            $"Error.Expected should be non-empty for expression '{expression}' but was '{result.Error.Expected}'");
    }

    /// <summary>
    /// Property 21d: For any invalid dice expression, the error has a non-empty Actual string.
    /// **Validates: Requirements 14.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DiceExpressionErrorPropertyTests) }, MaxTest = 500)]
    public void InvalidExpression_ErrorHasNonEmptyActual(string expression)
    {
        var result = DiceExpressionParser.Parse(expression);

        Assert.False(result.IsSuccess,
            $"Expected failure for invalid expression '{expression}' but got success");
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Actual),
            $"Error.Actual should be non-empty for expression '{expression}' but was '{result.Error.Actual}'");
    }
}
