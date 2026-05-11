using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class ActionEconomyTrackerTests
{
    private readonly ActionEconomyTracker _tracker = new();

    // ── Test Data ─────────────────────────────────────────────────────────────

    private static ActionEconomyDefinition Dnd5eEconomy => new()
    {
        Type = ActionEconomyType.NamedSlots,
        TurnStructure = new TurnStructure
        {
            Slots =
            [
                new ActionSlot { Name = "action", Label = "Action", Count = 1 },
                new ActionSlot { Name = "bonus_action", Label = "Bonus Action", Count = 1 },
                new ActionSlot { Name = "reaction", Label = "Reaction", Count = 1, ResetOn = "turn_start" },
                new ActionSlot { Name = "free_action", Label = "Free Action", Count = -1 }
            ]
        }
    };

    private static ActionEconomyDefinition ActionPointEconomy => new()
    {
        Type = ActionEconomyType.ActionPoints,
        PointsPerTurn = 6
    };

    private static ActionEconomyDefinition Pf2eEconomy => new()
    {
        Type = ActionEconomyType.MultiActionPenalty,
        MaxActions = 3,
        PenaltyIncrement = -5
    };

    private static ActionEconomyDefinition FreeformEconomy => new()
    {
        Type = ActionEconomyType.Freeform
    };

    // ── GetTurnBudget: Named Slots ────────────────────────────────────────────

    [Fact]
    public void GetTurnBudget_NamedSlots_ContainsAllDefinedSlots()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        Assert.Equal(4, budget.RemainingSlots.Count);
        Assert.Equal(1, budget.RemainingSlots["action"]);
        Assert.Equal(1, budget.RemainingSlots["bonus_action"]);
        Assert.Equal(1, budget.RemainingSlots["reaction"]);
        Assert.Equal(-1, budget.RemainingSlots["free_action"]); // Unlimited
    }

    [Fact]
    public void GetTurnBudget_NamedSlots_NotExhausted()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.ActionsUsed);
    }

    // ── GetTurnBudget: Action Points ──────────────────────────────────────────

    [Fact]
    public void GetTurnBudget_ActionPoints_HasCorrectPointPool()
    {
        var budget = _tracker.GetTurnBudget(ActionPointEconomy);

        Assert.Equal(6, budget.RemainingPoints);
        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.ActionsUsed);
    }

    // ── GetTurnBudget: Multi-Action Penalty ───────────────────────────────────

    [Fact]
    public void GetTurnBudget_MultiActionPenalty_HasMaxActions()
    {
        var budget = _tracker.GetTurnBudget(Pf2eEconomy);

        Assert.Equal(3, budget.RemainingSlots["action"]);
        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.ActionsUsed);
    }

    // ── GetTurnBudget: Freeform ───────────────────────────────────────────────

    [Fact]
    public void GetTurnBudget_Freeform_NeverExhausted()
    {
        var budget = _tracker.GetTurnBudget(FreeformEconomy);

        Assert.False(budget.IsExhausted);
        Assert.Empty(budget.RemainingSlots);
        Assert.Null(budget.RemainingPoints);
    }

    // ── ConsumeAction: Named Slots ────────────────────────────────────────────

    [Fact]
    public void ConsumeAction_NamedSlot_DecreasesCount()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        var updated = _tracker.ConsumeAction(budget, "action");

        Assert.Equal(0, updated.RemainingSlots["action"]);
        Assert.Equal(1, updated.RemainingSlots["bonus_action"]); // Unchanged
        Assert.Equal(1, updated.ActionsUsed);
    }

    [Fact]
    public void ConsumeAction_UnlimitedSlot_DoesNotDecrement()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        var updated = _tracker.ConsumeAction(budget, "free_action");

        Assert.Equal(-1, updated.RemainingSlots["free_action"]); // Still unlimited
        Assert.Equal(1, updated.ActionsUsed);
    }

    [Fact]
    public void ConsumeAction_ExhaustedSlot_ThrowsInvalidOperation()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);
        var afterAction = _tracker.ConsumeAction(budget, "action");

        Assert.Throws<InvalidOperationException>(() =>
            _tracker.ConsumeAction(afterAction, "action"));
    }

    [Fact]
    public void ConsumeAction_UnknownSlot_ThrowsInvalidOperation()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        Assert.Throws<InvalidOperationException>(() =>
            _tracker.ConsumeAction(budget, "legendary_action"));
    }

    // ── ConsumeAction: Action Points ──────────────────────────────────────────

    [Fact]
    public void ConsumeAction_ActionPoints_DecreasesPool()
    {
        var budget = _tracker.GetTurnBudget(ActionPointEconomy);

        var updated = _tracker.ConsumeAction(budget, "attack");

        Assert.Equal(5, updated.RemainingPoints);
        Assert.Equal(1, updated.ActionsUsed);
        Assert.False(updated.IsExhausted);
    }

    [Fact]
    public void ConsumeAction_ActionPoints_ExhaustsWhenZero()
    {
        var budget = new ActionBudget
        {
            RemainingPoints = 1,
            ActionsUsed = 5,
            IsExhausted = false
        };

        var updated = _tracker.ConsumeAction(budget, "move");

        Assert.Equal(0, updated.RemainingPoints);
        Assert.True(updated.IsExhausted);
    }

    [Fact]
    public void ConsumeAction_ActionPoints_RejectsWhenExhausted()
    {
        var budget = new ActionBudget
        {
            RemainingPoints = 0,
            ActionsUsed = 6,
            IsExhausted = true
        };

        Assert.Throws<InvalidOperationException>(() =>
            _tracker.ConsumeAction(budget, "attack"));
    }

    // ── ConsumeAction: Multi-Action Penalty ───────────────────────────────────

    [Fact]
    public void ConsumeAction_MultiActionPenalty_DecreasesActions()
    {
        var budget = _tracker.GetTurnBudget(Pf2eEconomy);

        var after1 = _tracker.ConsumeAction(budget, "action");
        Assert.Equal(2, after1.RemainingSlots["action"]);
        Assert.Equal(1, after1.ActionsUsed);

        var after2 = _tracker.ConsumeAction(after1, "action");
        Assert.Equal(1, after2.RemainingSlots["action"]);
        Assert.Equal(2, after2.ActionsUsed);

        var after3 = _tracker.ConsumeAction(after2, "action");
        Assert.Equal(0, after3.RemainingSlots["action"]);
        Assert.Equal(3, after3.ActionsUsed);
        Assert.True(after3.IsExhausted);
    }

    [Fact]
    public void ConsumeAction_MultiActionPenalty_RejectsWhenAllUsed()
    {
        var budget = _tracker.GetTurnBudget(Pf2eEconomy);
        var after1 = _tracker.ConsumeAction(budget, "action");
        var after2 = _tracker.ConsumeAction(after1, "action");
        var after3 = _tracker.ConsumeAction(after2, "action");

        Assert.Throws<InvalidOperationException>(() =>
            _tracker.ConsumeAction(after3, "action"));
    }

    // ── CanPerformAction ──────────────────────────────────────────────────────

    [Fact]
    public void CanPerformAction_AvailableSlot_ReturnsTrue()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        Assert.True(_tracker.CanPerformAction(budget, "action"));
        Assert.True(_tracker.CanPerformAction(budget, "bonus_action"));
        Assert.True(_tracker.CanPerformAction(budget, "free_action"));
    }

    [Fact]
    public void CanPerformAction_ExhaustedSlot_ReturnsFalse()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);
        var updated = _tracker.ConsumeAction(budget, "action");

        Assert.False(_tracker.CanPerformAction(updated, "action"));
    }

    [Fact]
    public void CanPerformAction_UnknownSlot_ReturnsFalse()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        Assert.False(_tracker.CanPerformAction(budget, "legendary_action"));
    }

    [Fact]
    public void CanPerformAction_EmptyName_ReturnsFalse()
    {
        var budget = _tracker.GetTurnBudget(Dnd5eEconomy);

        Assert.False(_tracker.CanPerformAction(budget, ""));
        Assert.False(_tracker.CanPerformAction(budget, "   "));
    }

    [Fact]
    public void CanPerformAction_ActionPoints_TrueWhenPointsRemain()
    {
        var budget = _tracker.GetTurnBudget(ActionPointEconomy);

        Assert.True(_tracker.CanPerformAction(budget, "any_action"));
    }

    [Fact]
    public void CanPerformAction_ActionPoints_FalseWhenExhausted()
    {
        var budget = new ActionBudget
        {
            RemainingPoints = 0,
            ActionsUsed = 6,
            IsExhausted = true
        };

        Assert.False(_tracker.CanPerformAction(budget, "any_action"));
    }
}
