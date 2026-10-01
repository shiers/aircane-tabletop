using Aircane.Domain.Combat;
using Xunit;

namespace Aircane.UnitTests.Combat;

/// <summary>
/// Unit tests for the pure <see cref="CombatEngine"/> state transitions.
/// </summary>
public class CombatEngineTests
{
    private static Combatant Pc(string id, int hp = 20, int maxHp = 20) => new()
    {
        Id = id,
        Name = $"PC-{id}",
        IsPlayerCharacter = true,
        CurrentHp = hp,
        MaxHp = maxHp,
    };

    private static Combatant Npc(string id, int hp = 10, int maxHp = 10) => new()
    {
        Id = id,
        Name = $"NPC-{id}",
        IsPlayerCharacter = false,
        CurrentHp = hp,
        MaxHp = maxHp,
    };

    // ── Start / initiative / turn order ───────────────────────────────────────

    [Fact]
    public void StartEncounter_IsInactiveUntilInitiativeRolled()
    {
        var state = CombatEngine.StartEncounter([Pc("a"), Npc("b")]);

        Assert.False(state.IsActive);
        Assert.Equal(0, state.Round);
        Assert.Equal(-1, state.TurnIndex);
        Assert.Empty(state.InitiativeOrder);
        Assert.Equal(2, state.Combatants.Count);
    }

    [Fact]
    public void RollInitiative_OrdersHighestFirst_AndActivates()
    {
        var state = CombatEngine.StartEncounter([Pc("a"), Npc("b"), Pc("c")]);

        state = CombatEngine.RollInitiative(state, new Dictionary<string, int>
        {
            ["a"] = 12,
            ["b"] = 20,
            ["c"] = 5,
        });

        Assert.True(state.IsActive);
        Assert.Equal(1, state.Round);
        Assert.Equal(0, state.TurnIndex);
        Assert.Equal(["b", "a", "c"], state.InitiativeOrder);
        Assert.Equal("b", state.ActiveCombatant!.Id);
    }

    [Fact]
    public void AdvanceTurn_MovesToNextCombatant()
    {
        var state = CombatEngine.RollInitiative(
            CombatEngine.StartEncounter([Pc("a"), Npc("b")]),
            new Dictionary<string, int> { ["a"] = 20, ["b"] = 10 });

        state = CombatEngine.AdvanceTurn(state);

        Assert.Equal(1, state.TurnIndex);
        Assert.Equal("b", state.ActiveCombatant!.Id);
        Assert.Equal(1, state.Round);
    }

    [Fact]
    public void AdvanceTurn_WrapsToNextRound_AndTicksConditions()
    {
        var state = CombatEngine.RollInitiative(
            CombatEngine.StartEncounter([Pc("a"), Npc("b")]),
            new Dictionary<string, int> { ["a"] = 20, ["b"] = 10 });

        state = CombatEngine.ApplyCondition(state, "a", "Prone", remainingRounds: 1);

        state = CombatEngine.AdvanceTurn(state); // -> b (turn 1, round 1)
        state = CombatEngine.AdvanceTurn(state); // wrap -> a (turn 0, round 2), tick

        Assert.Equal(2, state.Round);
        Assert.Equal(0, state.TurnIndex);
        // The 1-round Prone condition ticked to 0 and was removed.
        Assert.Empty(state.Combatants.First(c => c.Id == "a").Conditions);
    }

    [Fact]
    public void AdvanceTurn_InactiveEncounter_IsNoOp()
    {
        var state = CombatEngine.StartEncounter([Pc("a")]);
        var after = CombatEngine.AdvanceTurn(state);
        Assert.Equal(state, after);
    }

    // ── Damage / healing ──────────────────────────────────────────────────────

    [Fact]
    public void ApplyDamage_ReducesHp_FlooredAtZero()
    {
        var state = CombatEngine.StartEncounter([Npc("b", hp: 10)]);
        state = CombatEngine.ApplyDamage(state, "b", 15);
        Assert.Equal(0, state.Combatants[0].CurrentHp);
    }

    [Fact]
    public void ApplyDamage_TemporaryHpAbsorbsFirst()
    {
        var state = CombatEngine.StartEncounter([Npc("b", hp: 10) with { TemporaryHp = 5 }]);
        state = CombatEngine.ApplyDamage(state, "b", 7);
        var c = state.Combatants[0];
        Assert.Equal(0, c.TemporaryHp);
        Assert.Equal(8, c.CurrentHp); // 5 temp absorbed, 2 to HP
    }

    [Fact]
    public void ApplyDamage_DownedPlayerCharacter_StartsDeathSaves()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 4)]);
        state = CombatEngine.ApplyDamage(state, "a", 10);
        var c = state.Combatants[0];
        Assert.Equal(0, c.CurrentHp);
        Assert.True(c.IsDowned);
        Assert.NotNull(c.DeathSaves);
        Assert.False(c.IsDead);
    }

    [Fact]
    public void ApplyDamage_DownedNpc_IsDead_NoDeathSaves()
    {
        var state = CombatEngine.StartEncounter([Npc("b", hp: 4)]);
        state = CombatEngine.ApplyDamage(state, "b", 10);
        var c = state.Combatants[0];
        Assert.True(c.IsDowned);
        Assert.True(c.IsDead);
        Assert.Null(c.DeathSaves);
    }

    [Fact]
    public void ApplyHealing_CapsAtMaxHp()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 15, maxHp: 20)]);
        state = CombatEngine.ApplyHealing(state, "a", 100);
        Assert.Equal(20, state.Combatants[0].CurrentHp);
    }

    [Fact]
    public void ApplyHealing_DownedPc_ClearsDeathSaves()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 4)]);
        state = CombatEngine.ApplyDamage(state, "a", 10); // downed, death saves started
        state = CombatEngine.ApplyHealing(state, "a", 5);
        var c = state.Combatants[0];
        Assert.Equal(5, c.CurrentHp);
        Assert.Null(c.DeathSaves);
    }

    // ── Conditions ────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyCondition_AddsInstance()
    {
        var state = CombatEngine.StartEncounter([Pc("a")]);
        state = CombatEngine.ApplyCondition(state, "a", "Poisoned", remainingRounds: 3);
        var cond = Assert.Single(state.Combatants[0].Conditions);
        Assert.Equal("Poisoned", cond.Name);
        Assert.Equal(3, cond.RemainingRounds);
    }

    [Fact]
    public void ApplyCondition_SameName_RefreshesDuration()
    {
        var state = CombatEngine.StartEncounter([Pc("a")]);
        state = CombatEngine.ApplyCondition(state, "a", "Poisoned", remainingRounds: 1);
        state = CombatEngine.ApplyCondition(state, "a", "poisoned", remainingRounds: 5);
        var cond = Assert.Single(state.Combatants[0].Conditions);
        Assert.Equal(5, cond.RemainingRounds);
    }

    [Fact]
    public void RemoveCondition_DropsIt_CaseInsensitive()
    {
        var state = CombatEngine.StartEncounter([Pc("a")]);
        state = CombatEngine.ApplyCondition(state, "a", "Prone");
        state = CombatEngine.RemoveCondition(state, "a", "PRONE");
        Assert.Empty(state.Combatants[0].Conditions);
    }

    [Fact]
    public void TickConditions_DecrementsTimed_KeepsIndefinite()
    {
        var state = CombatEngine.StartEncounter([Pc("a")]);
        state = CombatEngine.ApplyCondition(state, "a", "Timed", remainingRounds: 2);
        state = CombatEngine.ApplyCondition(state, "a", "Forever", remainingRounds: null);

        state = CombatEngine.TickConditions(state);

        var conds = state.Combatants[0].Conditions;
        Assert.Equal(2, conds.Count);
        Assert.Equal(1, conds.First(c => c.Name == "Timed").RemainingRounds);
        Assert.Null(conds.First(c => c.Name == "Forever").RemainingRounds);
    }

    [Fact]
    public void TickConditions_RemovesExpired()
    {
        var state = CombatEngine.StartEncounter([Pc("a")]);
        state = CombatEngine.ApplyCondition(state, "a", "Brief", remainingRounds: 1);
        state = CombatEngine.TickConditions(state);
        Assert.Empty(state.Combatants[0].Conditions);
    }

    // ── Death saves ───────────────────────────────────────────────────────────

    [Fact]
    public void RecordDeathSave_ThreeFailures_Kills()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 1)]);
        state = CombatEngine.ApplyDamage(state, "a", 5); // downed

        state = CombatEngine.RecordDeathSave(state, "a", success: false);
        state = CombatEngine.RecordDeathSave(state, "a", success: false);
        state = CombatEngine.RecordDeathSave(state, "a", success: false);

        var c = state.Combatants[0];
        Assert.Equal(3, c.DeathSaves!.Failures);
        Assert.True(c.IsDead);
    }

    [Fact]
    public void RecordDeathSave_ThreeSuccesses_Stabilizes()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 1)]);
        state = CombatEngine.ApplyDamage(state, "a", 5);

        state = CombatEngine.RecordDeathSave(state, "a", success: true);
        state = CombatEngine.RecordDeathSave(state, "a", success: true);
        state = CombatEngine.RecordDeathSave(state, "a", success: true);

        var c = state.Combatants[0];
        Assert.True(c.DeathSaves!.IsStable);
        Assert.False(c.IsDead);
    }

    [Fact]
    public void RecordDeathSave_RecoversHp_ClearsSaves_And_Revives()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 1)]);
        state = CombatEngine.ApplyDamage(state, "a", 5);
        state = CombatEngine.RecordDeathSave(state, "a", success: false, recoversHp: true);
        var c = state.Combatants[0];
        Assert.Equal(1, c.CurrentHp);
        Assert.Null(c.DeathSaves);
        Assert.False(c.IsDowned);
    }

    [Fact]
    public void RecordDeathSave_OnHealthyCombatant_IsNoOp()
    {
        var state = CombatEngine.StartEncounter([Pc("a", hp: 20)]);
        state = CombatEngine.RecordDeathSave(state, "a", success: false);
        Assert.Null(state.Combatants[0].DeathSaves);
    }

    [Fact]
    public void Transitions_DoNotMutateInput()
    {
        var start = CombatEngine.StartEncounter([Pc("a", hp: 10)]);
        var after = CombatEngine.ApplyDamage(start, "a", 5);
        // Original record is unchanged (records + fresh lists).
        Assert.Equal(10, start.Combatants[0].CurrentHp);
        Assert.Equal(5, after.Combatants[0].CurrentHp);
    }
}
