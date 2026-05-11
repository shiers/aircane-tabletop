namespace Aircane.Application.DTOs.Dice;

/// <summary>
/// The result of parsing a dice expression string.
/// </summary>
public sealed record ParsedDiceExpression(
    string OriginalFormula,
    int DiceCount,
    int DiceSides,
    int Modifier,
    bool IsValid,
    string? ParseError = null);
