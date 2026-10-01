namespace Aircane.Application.DTOs.Dice;

/// <summary>
/// The full result of a dice roll, including kept and dropped die values.
/// </summary>
public sealed record RollResult(
    RollDto Roll,
    int[] AllDieResults,
    int[] KeptResults,
    int[] DroppedResults,
    int Total);
