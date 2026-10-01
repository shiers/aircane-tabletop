namespace Aircane.Application.GameSystems;

/// <summary>
/// Represents the remaining action budget for a participant's turn.
/// Tracks named slots, action points, or multi-action penalty state.
/// </summary>
public record ActionBudget
{
    /// <summary>
    /// Remaining count for each named action slot.
    /// For named slot systems: e.g., {"action": 1, "bonus_action": 1, "reaction": 1}.
    /// For multi-action penalty systems: e.g., {"action": 3} representing remaining actions.
    /// </summary>
    public IReadOnlyDictionary<string, int> RemainingSlots { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// For action point systems: the remaining points available this turn.
    /// Null for non-point-based systems.
    /// </summary>
    public int? RemainingPoints { get; init; }

    /// <summary>
    /// For multi-action penalty systems: the number of actions already used this turn.
    /// Used to calculate the cumulative penalty.
    /// </summary>
    public int ActionsUsed { get; init; }

    /// <summary>
    /// Whether the budget is fully exhausted (no more actions can be taken).
    /// </summary>
    public bool IsExhausted { get; init; }
}
