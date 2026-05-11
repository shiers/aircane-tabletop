using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for the ConditionRegistry.
/// Property 14: Condition registration and enforcement.
/// **Validates: Requirements 5.1, 5.2**
/// </summary>
public class ConditionRegistryPropertyTests
{
    private static readonly ConditionRegistry Registry = new();

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Test input for Property 14: a condition set with N conditions.
    /// </summary>
    public record ConditionSetTestInput(IReadOnlyList<ConditionDefinition> Conditions);

    /// <summary>
    /// Generates a valid ConditionEffect.
    /// </summary>
    private static Gen<ConditionEffect> ConditionEffectGen =>
        from type in Gen.Elements("roll_modifier", "grant_advantage", "damage_resistance", "speed_reduction")
        from scope in Gen.Elements("attack_rolls", "ability_checks", "saving_throws", "melee_attacks_against")
        from effect in Gen.Elements("disadvantage", "advantage", "immunity", "resistance")
        select new ConditionEffect { Type = type, Scope = scope, Effect = effect };

    /// <summary>
    /// Generates a valid ConditionDefinition with a unique name.
    /// </summary>
    private static Gen<ConditionDefinition> ConditionDefinitionGen(int index) =>
        from effectCount in Gen.Choose(1, 3)
        from effects in Gen.ListOf(effectCount, ConditionEffectGen)
        from durationType in Gen.Elements("rounds", "until_save", "until_action", "until_rest")
        from stackable in Arb.Generate<bool>()
        select new ConditionDefinition
        {
            Name = $"Condition_{index}",
            Description = $"Description for condition {index}",
            Effects = effects.ToList(),
            DurationType = durationType,
            Stackable = stackable
        };

    /// <summary>
    /// Generates a condition set with 1-20 unique conditions.
    /// </summary>
    private static Gen<ConditionSetTestInput> ConditionSetGen =>
        from count in Gen.Choose(1, 20)
        from conditions in Gen.Sequence(
            Enumerable.Range(0, count).Select(i => ConditionDefinitionGen(i)))
        select new ConditionSetTestInput(conditions.ToList());

    public static Arbitrary<ConditionSetTestInput> ConditionSetTestInputArbitrary =>
        Arb.From(ConditionSetGen);

    // ── Property 14a: All N conditions retrievable by name ────────────────────

    /// <summary>
    /// Property 14a: For any Condition Set with N conditions, after registration
    /// all N conditions SHALL be retrievable by name.
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConditionRegistryPropertyTests) }, MaxTest = 200)]
    public void AllConditions_AreRetrievableByName(ConditionSetTestInput input)
    {
        var conditions = input.Conditions;

        // All N conditions should be returned by GetConditions
        var allConditions = Registry.GetConditions(conditions);
        Assert.Equal(conditions.Count, allConditions.Count);

        // Each condition should be retrievable by its name
        foreach (var condition in conditions)
        {
            Assert.True(Registry.IsValidCondition(conditions, condition.Name),
                $"Condition '{condition.Name}' should be valid but was not found.");

            var retrieved = Registry.GetCondition(conditions, condition.Name);
            Assert.NotNull(retrieved);
            Assert.Equal(condition.Name, retrieved.Name);
            Assert.Equal(condition.Description, retrieved.Description);
            Assert.Equal(condition.Effects.Count, retrieved.Effects.Count);
        }
    }

    // ── Property 14b: Conditions with roll modifier effects ───────────────────

    /// <summary>
    /// Test input for verifying condition effects are correctly associated.
    /// </summary>
    public record ConditionWithEffectsInput(
        IReadOnlyList<ConditionDefinition> Conditions,
        int TargetIndex);

    private static Gen<ConditionWithEffectsInput> ConditionWithEffectsGen =>
        from count in Gen.Choose(1, 10)
        from conditions in Gen.Sequence(
            Enumerable.Range(0, count).Select(i => ConditionDefinitionGen(i)))
        let conditionList = conditions.ToList()
        from targetIndex in Gen.Choose(0, conditionList.Count - 1)
        select new ConditionWithEffectsInput(conditionList, targetIndex);

    public static Arbitrary<ConditionWithEffectsInput> ConditionWithEffectsInputArbitrary =>
        Arb.From(ConditionWithEffectsGen);

    /// <summary>
    /// Property 14b: For any registered condition with roll modifier effects,
    /// the retrieved condition SHALL contain the defined effects that can modify
    /// roll resolution according to the defined effect.
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConditionRegistryPropertyTests) }, MaxTest = 200)]
    public void RegisteredCondition_WithRollModifierEffects_ReturnsCorrectEffects(ConditionWithEffectsInput input)
    {
        var conditions = input.Conditions;
        var targetCondition = conditions[input.TargetIndex];

        var retrieved = Registry.GetCondition(conditions, targetCondition.Name);

        Assert.NotNull(retrieved);

        // All effects from the original definition should be present
        var rollModifierEffects = targetCondition.Effects
            .Where(e => e.Type == "roll_modifier")
            .ToList();

        var retrievedRollModifiers = retrieved.Effects
            .Where(e => e.Type == "roll_modifier")
            .ToList();

        Assert.Equal(rollModifierEffects.Count, retrievedRollModifiers.Count);

        // Each roll modifier effect should have a valid scope and effect value
        foreach (var effect in retrievedRollModifiers)
        {
            Assert.False(string.IsNullOrEmpty(effect.Scope),
                "Roll modifier effect must have a scope.");
            Assert.False(string.IsNullOrEmpty(effect.Effect),
                "Roll modifier effect must have an effect value.");
        }
    }
}
