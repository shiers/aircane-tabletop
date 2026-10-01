using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for the ActionEconomyTracker.
/// Property 15: Action economy budget tracking.
/// **Validates: Requirements 6.1, 6.3**
/// </summary>
public class ActionEconomyPropertyTests
{
    private static readonly ActionEconomyTracker Tracker = new();

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Test input for named slots economy.
    /// </summary>
    public record NamedSlotsInput(IReadOnlyList<ActionSlot> Slots);

    /// <summary>
    /// Generates a valid named slots economy with 1-6 unique slots.
    /// </summary>
    private static Gen<NamedSlotsInput> NamedSlotsGen =>
        from count in Gen.Choose(1, 6)
        from slots in Gen.Sequence(
            Enumerable.Range(0, count).Select(i =>
                from slotCount in Gen.Choose(1, 5)
                select new ActionSlot
                {
                    Name = $"slot_{i}",
                    Label = $"Slot {i}",
                    Count = slotCount
                }))
        select new NamedSlotsInput(slots.ToList());

    public static Arbitrary<NamedSlotsInput> NamedSlotsInputArbitrary =>
        Arb.From(NamedSlotsGen);

    /// <summary>
    /// Test input for action point economy.
    /// </summary>
    public record ActionPointsInput(int PointsPerTurn);

    private static Gen<ActionPointsInput> ActionPointsGen =>
        from points in Gen.Choose(1, 20)
        select new ActionPointsInput(points);

    public static Arbitrary<ActionPointsInput> ActionPointsInputArbitrary =>
        Arb.From(ActionPointsGen);

    /// <summary>
    /// Test input for multi-action penalty economy.
    /// </summary>
    public record MultiActionInput(int MaxActions, int PenaltyIncrement);

    private static Gen<MultiActionInput> MultiActionGen =>
        from maxActions in Gen.Choose(1, 6)
        from penalty in Gen.Choose(1, 10)
        select new MultiActionInput(maxActions, -penalty);

    public static Arbitrary<MultiActionInput> MultiActionInputArbitrary =>
        Arb.From(MultiActionGen);

    // ══════════════════════════════════════════════════════════════════════════
    // Property 15a: Initial turn budget contains all defined action slots
    // **Validates: Requirements 6.1**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 15a: For any action economy definition with named slots,
    /// the initial turn budget SHALL contain all defined action slots with their specified counts.
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void NamedSlots_InitialBudget_ContainsAllSlotsWithCorrectCounts(NamedSlotsInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure { Slots = input.Slots.ToList() }
        };

        var budget = Tracker.GetTurnBudget(economy);

        // Budget should contain all defined slots
        Assert.Equal(input.Slots.Count, budget.RemainingSlots.Count);

        // Each slot should have its specified count
        foreach (var slot in input.Slots)
        {
            Assert.True(budget.RemainingSlots.ContainsKey(slot.Name),
                $"Budget should contain slot '{slot.Name}'");
            Assert.Equal(slot.Count, budget.RemainingSlots[slot.Name]);
        }

        // Budget should not be exhausted initially
        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.ActionsUsed);
    }

    /// <summary>
    /// Property 15a (action points variant): For any action point economy,
    /// the initial budget SHALL contain the specified point pool.
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void ActionPoints_InitialBudget_HasCorrectPointPool(ActionPointsInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.ActionPoints,
            PointsPerTurn = input.PointsPerTurn
        };

        var budget = Tracker.GetTurnBudget(economy);

        Assert.Equal(input.PointsPerTurn, budget.RemainingPoints);
        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.ActionsUsed);
    }

    /// <summary>
    /// Property 15a (multi-action variant): For any multi-action penalty economy,
    /// the initial budget SHALL contain the max actions count.
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void MultiAction_InitialBudget_HasMaxActions(MultiActionInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.MultiActionPenalty,
            MaxActions = input.MaxActions,
            PenaltyIncrement = input.PenaltyIncrement
        };

        var budget = Tracker.GetTurnBudget(economy);

        Assert.Equal(input.MaxActions, budget.RemainingSlots["action"]);
        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.ActionsUsed);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 15b: Consuming an action decreases the count by one
    // **Validates: Requirements 6.1, 6.3**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test input for consuming actions from named slots.
    /// </summary>
    public record ConsumeNamedSlotInput(IReadOnlyList<ActionSlot> Slots, int TargetSlotIndex);

    private static Gen<ConsumeNamedSlotInput> ConsumeNamedSlotGen =>
        from count in Gen.Choose(1, 6)
        from slots in Gen.Sequence(
            Enumerable.Range(0, count).Select(i =>
                from slotCount in Gen.Choose(1, 5)
                select new ActionSlot
                {
                    Name = $"slot_{i}",
                    Label = $"Slot {i}",
                    Count = slotCount
                }))
        let slotList = slots.ToList()
        from targetIndex in Gen.Choose(0, slotList.Count - 1)
        select new ConsumeNamedSlotInput(slotList, targetIndex);

    public static Arbitrary<ConsumeNamedSlotInput> ConsumeNamedSlotInputArbitrary =>
        Arb.From(ConsumeNamedSlotGen);

    /// <summary>
    /// Property 15b: After consuming an action slot, the remaining count for that slot
    /// SHALL decrease by one.
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void ConsumeAction_NamedSlot_DecreasesCountByOne(ConsumeNamedSlotInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure { Slots = input.Slots.ToList() }
        };

        var budget = Tracker.GetTurnBudget(economy);
        var targetSlot = input.Slots[input.TargetSlotIndex];
        var originalCount = budget.RemainingSlots[targetSlot.Name];

        var updated = Tracker.ConsumeAction(budget, targetSlot.Name);

        Assert.Equal(originalCount - 1, updated.RemainingSlots[targetSlot.Name]);
        Assert.Equal(1, updated.ActionsUsed);
    }

    /// <summary>
    /// Property 15b (action points variant): After consuming an action,
    /// the action point pool SHALL decrease by the action's cost (1 point).
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void ConsumeAction_ActionPoints_DecreasesPoolByOne(ActionPointsInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.ActionPoints,
            PointsPerTurn = input.PointsPerTurn
        };

        var budget = Tracker.GetTurnBudget(economy);

        var updated = Tracker.ConsumeAction(budget, "any_action");

        Assert.Equal(input.PointsPerTurn - 1, updated.RemainingPoints);
        Assert.Equal(1, updated.ActionsUsed);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 15c: Consuming a slot with zero remaining is rejected
    // **Validates: Requirements 6.1, 6.3**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 15c: Attempting to consume a slot with zero remaining SHALL be rejected.
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void ConsumeAction_ExhaustedSlot_IsRejected(ConsumeNamedSlotInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure { Slots = input.Slots.ToList() }
        };

        var budget = Tracker.GetTurnBudget(economy);
        var targetSlot = input.Slots[input.TargetSlotIndex];

        // Consume the slot until it's exhausted
        var current = budget;
        for (var i = 0; i < targetSlot.Count; i++)
        {
            current = Tracker.ConsumeAction(current, targetSlot.Name);
        }

        // Verify the slot is now at zero
        Assert.Equal(0, current.RemainingSlots[targetSlot.Name]);

        // Attempting to consume again should throw
        Assert.Throws<InvalidOperationException>(() =>
            Tracker.ConsumeAction(current, targetSlot.Name));
    }

    /// <summary>
    /// Property 15c (action points variant): Attempting to consume when points are zero
    /// SHALL be rejected.
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ActionEconomyPropertyTests) }, MaxTest = 200)]
    public void ConsumeAction_ExhaustedPoints_IsRejected(ActionPointsInput input)
    {
        var economy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.ActionPoints,
            PointsPerTurn = input.PointsPerTurn
        };

        var budget = Tracker.GetTurnBudget(economy);

        // Consume all points
        var current = budget;
        for (var i = 0; i < input.PointsPerTurn; i++)
        {
            current = Tracker.ConsumeAction(current, "action");
        }

        // Verify exhausted
        Assert.Equal(0, current.RemainingPoints);
        Assert.True(current.IsExhausted);

        // Attempting to consume again should throw
        Assert.Throws<InvalidOperationException>(() =>
            Tracker.ConsumeAction(current, "action"));
    }
}
