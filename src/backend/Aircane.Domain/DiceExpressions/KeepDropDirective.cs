namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// Describes a keep or drop directive for dice rolls.
/// </summary>
public record KeepDropDirective
{
    /// <summary>The type of keep/drop operation.</summary>
    public KeepDropType Type { get; init; }

    /// <summary>The number of dice to keep or drop.</summary>
    public int Amount { get; init; }
}

/// <summary>
/// The type of keep/drop operation.
/// </summary>
public enum KeepDropType
{
    /// <summary>Keep the N highest dice.</summary>
    KeepHighest,

    /// <summary>Keep the N lowest dice.</summary>
    KeepLowest,

    /// <summary>Drop the N highest dice.</summary>
    DropHighest,

    /// <summary>Drop the N lowest dice.</summary>
    DropLowest
}
