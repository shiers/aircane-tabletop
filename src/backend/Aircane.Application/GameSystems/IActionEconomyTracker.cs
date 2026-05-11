using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Tracks action economy budgets per turn, supporting named slots, action points,
/// multi-action penalty, and freeform systems.
/// </summary>
public interface IActionEconomyTracker
{
    /// <summary>
    /// Returns the initial action budget for a turn based on the action economy definition.
    /// </summary>
    ActionBudget GetTurnBudget(ActionEconomyDefinition economy);

    /// <summary>
    /// Consumes an action slot from the current budget and returns the updated budget.
    /// Throws InvalidOperationException if the action cannot be performed.
    /// </summary>
    ActionBudget ConsumeAction(ActionBudget current, string actionSlotName);

    /// <summary>
    /// Validates whether a proposed action is permitted given the remaining budget.
    /// </summary>
    bool CanPerformAction(ActionBudget current, string actionSlotName);
}
