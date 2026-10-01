using System.Text.RegularExpressions;

namespace Aircane.Application.Dice;

/// <summary>
/// Parses dice expression strings into <see cref="DiceExpression"/> value objects.
/// </summary>
/// <remarks>
/// Supported formats (all case-insensitive):
/// <list type="bullet">
///   <item><c>d20</c> → 1d20+0</item>
///   <item><c>1d20</c> → 1d20+0</item>
///   <item><c>1d20+5</c> → 1d20 with +5 modifier</item>
///   <item><c>2d6+3</c> → 2d6 with +3 modifier</item>
///   <item><c>4d6kh3</c> → 4d6 keep highest 3</item>
///   <item><c>4d6kl3</c> → 4d6 keep lowest 3</item>
///   <item><c>advantage</c> → 2d20 keep highest 1</item>
///   <item><c>disadvantage</c> → 2d20 keep lowest 1</item>
///   <item><c>1d20-2</c> → 1d20 with -2 modifier</item>
/// </list>
/// Validation constraints:
/// <list type="bullet">
///   <item>diceCount: 1–100</item>
///   <item>dieSides: 2–1000</item>
///   <item>modifier: -999 to +999</item>
///   <item>keepN must be ≥ 1 and ≤ diceCount</item>
/// </list>
/// </remarks>
public static class DiceParser
{
    // Matches: [N]dM[kh|kl N][+/-modifier]
    // Examples: d20, 1d20, 2d6+3, 4d6kh3, 4d6kl2, 1d20-5
    private static readonly Regex DiceRegex = new(
        @"^(?<count>\d+)?d(?<sides>\d+)(?:k(?<keeptype>h|l)(?<keepn>\d+))?(?:(?<sign>[+\-])(?<mod>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MinDiceCount = 1;
    private const int MaxDiceCount = 100;
    private const int MinDieSides = 2;
    private const int MaxDieSides = 1000;
    private const int MinModifier = -999;
    private const int MaxModifier = 999;

    /// <summary>
    /// Parses a dice expression string and returns a <see cref="DiceExpression"/>.
    /// </summary>
    /// <param name="expression">The dice expression to parse (e.g. "2d6+3", "advantage").</param>
    /// <returns>A valid <see cref="DiceExpression"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the expression is null, empty, or does not match a supported format,
    /// or when the parsed values are outside the allowed ranges.
    /// </exception>
    public static DiceExpression Parse(string expression)
    {
        if (!TryParse(expression, out var result, out var error))
            throw new ArgumentException(error, nameof(expression));

        return result!;
    }

    /// <summary>
    /// Attempts to parse a dice expression string.
    /// </summary>
    /// <param name="expression">The dice expression to parse.</param>
    /// <param name="result">
    /// When this method returns <c>true</c>, contains the parsed <see cref="DiceExpression"/>;
    /// otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if parsing succeeded; <c>false</c> otherwise.</returns>
    public static bool TryParse(string expression, out DiceExpression? result)
        => TryParse(expression, out result, out _);

    // ── Internal implementation ───────────────────────────────────────────────

    private static bool TryParse(
        string expression,
        out DiceExpression? result,
        out string error)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Dice expression must not be null or empty.";
            return false;
        }

        var trimmed = expression.Trim();

        // Handle advantage / disadvantage shorthands
        if (trimmed.Equals("advantage", StringComparison.OrdinalIgnoreCase))
        {
            result = DiceExpression.WithKeepHighest(
                originalExpression: trimmed,
                diceCount: 2,
                dieSides: 20,
                modifier: 0,
                keepHighest: 1,
                isAdvantage: true);
            error = string.Empty;
            return true;
        }

        if (trimmed.Equals("disadvantage", StringComparison.OrdinalIgnoreCase))
        {
            result = DiceExpression.WithKeepLowest(
                originalExpression: trimmed,
                diceCount: 2,
                dieSides: 20,
                modifier: 0,
                keepLowest: 1,
                isDisadvantage: true);
            error = string.Empty;
            return true;
        }

        var match = DiceRegex.Match(trimmed);
        if (!match.Success)
        {
            error = $"'{trimmed}' is not a valid dice expression. " +
                    "Expected formats: d20, 1d20, 2d6+3, 4d6kh3, advantage, disadvantage.";
            return false;
        }

        // Dice count defaults to 1 when omitted (e.g. "d20")
        var diceCount = match.Groups["count"].Success
            ? int.Parse(match.Groups["count"].Value)
            : 1;

        var dieSides = int.Parse(match.Groups["sides"].Value);

        // Modifier
        var modifier = 0;
        if (match.Groups["sign"].Success && match.Groups["mod"].Success)
        {
            var modValue = int.Parse(match.Groups["mod"].Value);
            modifier = match.Groups["sign"].Value == "-" ? -modValue : modValue;
        }

        // Keep highest / lowest
        int? keepHighest = null;
        int? keepLowest = null;
        if (match.Groups["keeptype"].Success && match.Groups["keepn"].Success)
        {
            var keepN = int.Parse(match.Groups["keepn"].Value);
            if (match.Groups["keeptype"].Value.Equals("h", StringComparison.OrdinalIgnoreCase))
                keepHighest = keepN;
            else
                keepLowest = keepN;
        }

        // Validate ranges
        if (diceCount < MinDiceCount || diceCount > MaxDiceCount)
        {
            error = $"Dice count must be between {MinDiceCount} and {MaxDiceCount}. Got: {diceCount}.";
            return false;
        }

        if (dieSides < MinDieSides || dieSides > MaxDieSides)
        {
            error = $"Die sides must be between {MinDieSides} and {MaxDieSides}. Got: {dieSides}.";
            return false;
        }

        if (modifier < MinModifier || modifier > MaxModifier)
        {
            error = $"Modifier must be between {MinModifier} and {MaxModifier}. Got: {modifier}.";
            return false;
        }

        if (keepHighest.HasValue)
        {
            if (keepHighest.Value < 1 || keepHighest.Value > diceCount)
            {
                error = $"Keep-highest value must be between 1 and the dice count ({diceCount}). Got: {keepHighest.Value}.";
                return false;
            }
        }

        if (keepLowest.HasValue)
        {
            if (keepLowest.Value < 1 || keepLowest.Value > diceCount)
            {
                error = $"Keep-lowest value must be between 1 and the dice count ({diceCount}). Got: {keepLowest.Value}.";
                return false;
            }
        }

        result = keepHighest.HasValue
            ? DiceExpression.WithKeepHighest(trimmed, diceCount, dieSides, modifier, keepHighest.Value)
            : keepLowest.HasValue
                ? DiceExpression.WithKeepLowest(trimmed, diceCount, dieSides, modifier, keepLowest.Value)
                : DiceExpression.Standard(trimmed, diceCount, dieSides, modifier);

        error = string.Empty;
        return true;
    }
}
