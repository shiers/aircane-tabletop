using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Implements action economy tracking for all supported economy types:
/// named slots, action points, multi-action penalty, and freeform.
/// </summary>
public class ActionEconomyTracker : IActionEconomyTracker
{
    /// <inheritdoc />
    public ActionBudget GetTurnBudget(ActionEconomyDefinition economy)
    {
        ArgumentNullException.ThrowIfNull(economy);

        return economy.Type switch
        {
            ActionEconomyType.NamedSlots => GetNamedSlotsBudget(economy),
            ActionEconomyType.ActionPoints => GetActionPointsBudget(economy),
            ActionEconomyType.MultiActionPenalty => GetMultiActionPenaltyBudget(economy),
            ActionEconomyType.Freeform => GetFreeformBudget(),
            _ => throw new ArgumentOutOfRangeException(nameof(economy), $"Unsupported action economy type: {economy.Type}")
        };
    }

    /// <inheritdoc />
    public ActionBudget ConsumeAction(ActionBudget current, string actionSlotName)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (string.IsNullOrWhiteSpace(actionSlotName))
            throw new ArgumentException("Action slot name cannot be empty.", nameof(actionSlotName));

        if (!CanPerformAction(current, actionSlotName))
            throw new InvalidOperationException(
                $"Cannot perform action '{actionSlotName}': budget exhausted or slot unavailable.");

        // Handle action point systems
        if (current.RemainingPoints.HasValue)
        {
            return ConsumeActionPoints(current, actionSlotName);
        }

        // Handle named slots and multi-action penalty
        return ConsumeNamedSlot(current, actionSlotName);
    }

    /// <inheritdoc />
    public bool CanPerformAction(ActionBudget current, string actionSlotName)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (string.IsNullOrWhiteSpace(actionSlotName))
            return false;

        if (current.IsExhausted)
            return false;

        // Action point systems: check if there are remaining points
        if (current.RemainingPoints.HasValue)
        {
            // Any action costs at least 1 point
            return current.RemainingPoints.Value > 0;
        }

        // Named slot / multi-action penalty: check if the slot exists and has remaining uses
        if (!current.RemainingSlots.TryGetValue(actionSlotName, out var remaining))
            return false;

        // -1 means unlimited
        if (remaining == -1)
            return true;

        return remaining > 0;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static ActionBudget GetNamedSlotsBudget(ActionEconomyDefinition economy)
    {
        var slots = new Dictionary<string, int>();

        if (economy.TurnStructure?.Slots is { Count: > 0 } actionSlots)
        {
            foreach (var slot in actionSlots)
            {
                slots[slot.Name] = slot.Count;
            }
        }

        return new ActionBudget
        {
            RemainingSlots = slots,
            RemainingPoints = null,
            ActionsUsed = 0,
            IsExhausted = slots.Count == 0
        };
    }

    private static ActionBudget GetActionPointsBudget(ActionEconomyDefinition economy)
    {
        var points = economy.PointsPerTurn ?? 0;

        return new ActionBudget
        {
            RemainingSlots = new Dictionary<string, int>(),
            RemainingPoints = points,
            ActionsUsed = 0,
            IsExhausted = points <= 0
        };
    }

    private static ActionBudget GetMultiActionPenaltyBudget(ActionEconomyDefinition economy)
    {
        var maxActions = economy.MaxActions ?? 3; // Default to 3 actions (PF2e standard)

        var slots = new Dictionary<string, int>
        {
            ["action"] = maxActions
        };

        return new ActionBudget
        {
            RemainingSlots = slots,
            RemainingPoints = null,
            ActionsUsed = 0,
            IsExhausted = maxActions <= 0
        };
    }

    private static ActionBudget GetFreeformBudget()
    {
        // Freeform: never exhausted, no tracked slots
        return new ActionBudget
        {
            RemainingSlots = new Dictionary<string, int>(),
            RemainingPoints = null,
            ActionsUsed = 0,
            IsExhausted = false
        };
    }

    private static ActionBudget ConsumeActionPoints(ActionBudget current, string actionSlotName)
    {
        // Each action costs 1 point by default
        var newPoints = current.RemainingPoints!.Value - 1;
        var newActionsUsed = current.ActionsUsed + 1;

        return current with
        {
            RemainingPoints = newPoints,
            ActionsUsed = newActionsUsed,
            IsExhausted = newPoints <= 0
        };
    }

    private static ActionBudget ConsumeNamedSlot(ActionBudget current, string actionSlotName)
    {
        var newSlots = new Dictionary<string, int>(current.RemainingSlots);
        var currentCount = newSlots[actionSlotName];

        // -1 means unlimited, don't decrement
        if (currentCount != -1)
        {
            newSlots[actionSlotName] = currentCount - 1;
        }

        var newActionsUsed = current.ActionsUsed + 1;

        // Check if all finite slots are exhausted
        var isExhausted = newSlots.All(kvp => kvp.Value == 0);

        return current with
        {
            RemainingSlots = newSlots,
            ActionsUsed = newActionsUsed,
            IsExhausted = isExhausted
        };
    }
}
