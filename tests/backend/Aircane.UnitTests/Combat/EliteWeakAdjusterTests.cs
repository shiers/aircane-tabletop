using Aircane.Domain.Combat;
using Xunit;

namespace Aircane.UnitTests.Combat;

/// <summary>
/// Unit tests for <see cref="EliteWeakAdjuster"/> (PF2e elite/weak creature templates).
/// </summary>
public class EliteWeakAdjusterTests
{
    private static EncounterState WithGoblin(int hp = 12) => new()
    {
        IsActive = true,
        Round = 1,
        TurnIndex = 0,
        InitiativeOrder = ["goblin"],
        Combatants =
        [
            new Combatant { Id = "goblin", Name = "Goblin", CurrentHp = hp, MaxHp = hp },
        ],
    };

    [Fact]
    public void ApplyElite_IncreasesHp_AndTagsElite()
    {
        var result = EliteWeakAdjuster.ApplyElite(WithGoblin(12), "goblin");
        var goblin = result.Combatants.Single();

        Assert.True(goblin.MaxHp > 12);
        Assert.Contains(goblin.Conditions, c => c.Name == EliteWeakAdjuster.EliteMarker);
    }

    [Fact]
    public void ApplyWeak_DecreasesHp_AndTagsWeak()
    {
        var result = EliteWeakAdjuster.ApplyWeak(WithGoblin(30), "goblin");
        var goblin = result.Combatants.Single();

        Assert.True(goblin.MaxHp < 30);
        Assert.True(goblin.MaxHp >= 1);
        Assert.Contains(goblin.Conditions, c => c.Name == EliteWeakAdjuster.WeakMarker);
    }

    [Fact]
    public void ApplyElite_IsIdempotent()
    {
        var once = EliteWeakAdjuster.ApplyElite(WithGoblin(12), "goblin");
        var twice = EliteWeakAdjuster.ApplyElite(once, "goblin");

        Assert.Equal(once.Combatants.Single().MaxHp, twice.Combatants.Single().MaxHp);
        Assert.Single(twice.Combatants.Single().Conditions.Where(c => c.Name == EliteWeakAdjuster.EliteMarker));
    }

    [Fact]
    public void ApplyWeak_RemovesEliteMarker()
    {
        var elite = EliteWeakAdjuster.ApplyElite(WithGoblin(30), "goblin");
        var weak = EliteWeakAdjuster.ApplyWeak(elite, "goblin");
        var goblin = weak.Combatants.Single();

        Assert.DoesNotContain(goblin.Conditions, c => c.Name == EliteWeakAdjuster.EliteMarker);
        Assert.Contains(goblin.Conditions, c => c.Name == EliteWeakAdjuster.WeakMarker);
    }

    [Fact]
    public void ApplyElite_UnknownCombatant_NoOp()
    {
        var state = WithGoblin(12);
        var result = EliteWeakAdjuster.ApplyElite(state, "does-not-exist");
        Assert.Equal(12, result.Combatants.Single().MaxHp);
    }
}
