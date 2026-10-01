using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Defines the action economy for a game system - what a character can do on their turn.
/// </summary>
public record ActionEconomyDefinition
{
    /// <summary>The type of action economy (named_slots, action_points, multi_action_penalty, freeform).</summary>
    public ActionEconomyType Type { get; init; }

    /// <summary>The turn structure defining available action slots. Null for freeform systems.</summary>
    public TurnStructure? TurnStructure { get; init; }

    /// <summary>For action point systems: the total points available per turn.</summary>
    public int? PointsPerTurn { get; init; }

    /// <summary>For multi-action penalty systems: the penalty increment per additional action.</summary>
    public int? PenaltyIncrement { get; init; }

    /// <summary>For multi-action penalty systems: the maximum number of actions per turn.</summary>
    public int? MaxActions { get; init; }
}

/// <summary>
/// Defines the structure of a turn with named action slots.
/// </summary>
public record TurnStructure
{
    /// <summary>The available action slots in a turn.</summary>
    public IReadOnlyList<ActionSlot> Slots { get; init; } = [];
}

/// <summary>
/// A single action slot within a turn structure.
/// </summary>
public record ActionSlot
{
    /// <summary>Internal name for this slot (e.g., "action", "bonus_action", "reaction").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Display label for this slot.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Number of times this slot can be used per turn. -1 means unlimited.</summary>
    public int Count { get; init; }

    /// <summary>When this slot resets (e.g., "turn_start", "round_start"). Null means resets at turn start.</summary>
    public string? ResetOn { get; init; }

    /// <summary>Optional resource this slot consumes (e.g., "speed" for movement).</summary>
    public string? Resource { get; init; }
}
