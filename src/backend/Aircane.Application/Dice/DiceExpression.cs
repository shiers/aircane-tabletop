namespace Aircane.Application.Dice;

/// <summary>
/// Represents a fully parsed dice expression as an immutable value object.
/// </summary>
/// <remarks>
/// Supported formats:
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
/// </remarks>
public sealed class DiceExpression
{
    /// <summary>Number of dice to roll (1–100).</summary>
    public int DiceCount { get; }

    /// <summary>Number of sides on each die (2–1000).</summary>
    public int DieSides { get; }

    /// <summary>Flat modifier added to the total (-999 to +999).</summary>
    public int Modifier { get; }

    /// <summary>
    /// When set, only the highest N dice results are summed.
    /// Mutually exclusive with <see cref="KeepLowest"/>.
    /// </summary>
    public int? KeepHighest { get; }

    /// <summary>
    /// When set, only the lowest N dice results are summed.
    /// Mutually exclusive with <see cref="KeepHighest"/>.
    /// </summary>
    public int? KeepLowest { get; }

    /// <summary>
    /// True when the expression was entered as the "advantage" shorthand
    /// (2d20 keep highest 1).
    /// </summary>
    public bool IsAdvantage { get; }

    /// <summary>
    /// True when the expression was entered as the "disadvantage" shorthand
    /// (2d20 keep lowest 1).
    /// </summary>
    public bool IsDisadvantage { get; }

    /// <summary>The original expression string as supplied by the caller.</summary>
    public string OriginalExpression { get; }

    private DiceExpression(
        string originalExpression,
        int diceCount,
        int dieSides,
        int modifier,
        int? keepHighest,
        int? keepLowest,
        bool isAdvantage,
        bool isDisadvantage)
    {
        OriginalExpression = originalExpression;
        DiceCount = diceCount;
        DieSides = dieSides;
        Modifier = modifier;
        KeepHighest = keepHighest;
        KeepLowest = keepLowest;
        IsAdvantage = isAdvantage;
        IsDisadvantage = isDisadvantage;
    }

    /// <summary>Creates a standard dice expression (no keep modifier).</summary>
    internal static DiceExpression Standard(
        string originalExpression,
        int diceCount,
        int dieSides,
        int modifier)
        => new(originalExpression, diceCount, dieSides, modifier,
               keepHighest: null, keepLowest: null,
               isAdvantage: false, isDisadvantage: false);

    /// <summary>Creates a keep-highest dice expression.</summary>
    internal static DiceExpression WithKeepHighest(
        string originalExpression,
        int diceCount,
        int dieSides,
        int modifier,
        int keepHighest,
        bool isAdvantage = false)
        => new(originalExpression, diceCount, dieSides, modifier,
               keepHighest: keepHighest, keepLowest: null,
               isAdvantage: isAdvantage, isDisadvantage: false);

    /// <summary>Creates a keep-lowest dice expression.</summary>
    internal static DiceExpression WithKeepLowest(
        string originalExpression,
        int diceCount,
        int dieSides,
        int modifier,
        int keepLowest,
        bool isDisadvantage = false)
        => new(originalExpression, diceCount, dieSides, modifier,
               keepHighest: null, keepLowest: keepLowest,
               isAdvantage: false, isDisadvantage: isDisadvantage);

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IsAdvantage) return "advantage";
        if (IsDisadvantage) return "disadvantage";

        var keep = KeepHighest.HasValue ? $"kh{KeepHighest}"
                 : KeepLowest.HasValue  ? $"kl{KeepLowest}"
                 : string.Empty;

        var mod = Modifier > 0  ? $"+{Modifier}"
                : Modifier < 0  ? $"{Modifier}"
                : string.Empty;

        return $"{DiceCount}d{DieSides}{keep}{mod}";
    }
}
