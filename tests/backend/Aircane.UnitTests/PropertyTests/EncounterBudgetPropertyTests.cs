using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for the EncounterBudgetEngine.
/// Property 16: Encounter budget computation and flagging.
/// **Validates: Requirements 7.1, 7.4, 7.5**
/// </summary>
public class EncounterBudgetPropertyTests
{
    private static readonly EncounterBudgetEngine Engine = new();

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Test input for encounter budget computation.
    /// </summary>
    public record BudgetInput(
        EncounterBudgetType BudgetType,
        IReadOnlyList<DifficultyTier> Tiers,
        PartyComposition Party);

    /// <summary>
    /// Test input for encounter validation.
    /// </summary>
    public record ValidationInput(
        EncounterBudgetType BudgetType,
        IReadOnlyList<DifficultyTier> Tiers,
        PartyComposition Party,
        IReadOnlyList<CreatureThreat> Creatures);

    /// <summary>
    /// Generates a valid party composition with 1-8 characters of levels 1-20.
    /// </summary>
    private static Gen<PartyComposition> PartyGen =>
        from size in Gen.Choose(1, 8)
        from levels in Gen.ListOf(size, Gen.Choose(1, 20))
        select new PartyComposition
        {
            PartySize = size,
            CharacterLevels = levels.ToList()
        };

    /// <summary>
    /// Generates 1-5 difficulty tiers with increasing multipliers.
    /// </summary>
    private static Gen<IReadOnlyList<DifficultyTier>> TiersGen =>
        from count in Gen.Choose(1, 5)
        from baseMultipliers in Gen.Sequence(
            Enumerable.Range(0, count).Select(i =>
                Gen.Choose(50 + i * 50, 100 + i * 100)))
        select (IReadOnlyList<DifficultyTier>)baseMultipliers
            .Select((m, i) => new DifficultyTier
            {
                Name = $"Tier_{i}",
                Multiplier = m / 100.0
            })
            .OrderBy(t => t.Multiplier)
            .ToList();

    /// <summary>
    /// Generates a budget type.
    /// </summary>
    private static Gen<EncounterBudgetType> BudgetTypeGen =>
        Gen.Elements(
            EncounterBudgetType.XpBudget,
            EncounterBudgetType.CreatureLevel,
            EncounterBudgetType.ThreatRating,
            EncounterBudgetType.NarrativeTiers);

    /// <summary>
    /// Generates a valid budget input.
    /// </summary>
    private static Gen<BudgetInput> BudgetInputGen =>
        from budgetType in BudgetTypeGen
        from tiers in TiersGen
        from party in PartyGen
        select new BudgetInput(budgetType, tiers, party);

    public static Arbitrary<BudgetInput> BudgetInputArbitrary =>
        Arb.From(BudgetInputGen);

    /// <summary>
    /// Generates creatures with costs between 1 and 5000.
    /// </summary>
    private static Gen<IReadOnlyList<CreatureThreat>> CreaturesGen =>
        from count in Gen.Choose(1, 6)
        from creatures in Gen.Sequence(
            Enumerable.Range(0, count).Select(i =>
                from cost in Gen.Choose(1, 5000)
                select new CreatureThreat
                {
                    Name = $"Creature_{i}",
                    Cost = cost
                }))
        select (IReadOnlyList<CreatureThreat>)creatures.ToList();

    /// <summary>
    /// Generates a validation input.
    /// </summary>
    private static Gen<ValidationInput> ValidationInputGen =>
        from budgetType in BudgetTypeGen
        from tiers in TiersGen
        from party in PartyGen
        from creatures in CreaturesGen
        select new ValidationInput(budgetType, tiers, party, creatures);

    public static Arbitrary<ValidationInput> ValidationInputArbitrary =>
        Arb.From(ValidationInputGen);

    // ══════════════════════════════════════════════════════════════════════════
    // Property 16a: Budget computation is deterministic
    // **Validates: Requirements 7.1**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 16a: For any encounter budget formula, party composition, and set of creatures,
    /// the computed budget SHALL be deterministic given the same inputs.
    /// **Validates: Requirements 7.1, 7.4, 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EncounterBudgetPropertyTests) }, MaxTest = 200)]
    public void ComputeBudget_IsDeterministic_SameInputsProduceSameOutput(BudgetInput input)
    {
        var formula = new EncounterBudgetFormula
        {
            Type = input.BudgetType,
            DifficultyTiers = input.Tiers.ToList()
        };

        var result1 = Engine.ComputeBudget(formula, input.Party);
        var result2 = Engine.ComputeBudget(formula, input.Party);

        Assert.Equal(result1.TierBudgets.Count, result2.TierBudgets.Count);
        foreach (var (tierName, budget1) in result1.TierBudgets)
        {
            Assert.True(result2.TierBudgets.ContainsKey(tierName));
            Assert.Equal(budget1, result2.TierBudgets[tierName]);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Property 16b: Encounters exceeding ceiling are flagged
    // **Validates: Requirements 7.4, 7.5**
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 16b: Any encounter whose total creature cost exceeds the party's budget
    /// for the configured difficulty ceiling SHALL be flagged.
    /// **Validates: Requirements 7.1, 7.4, 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(EncounterBudgetPropertyTests) }, MaxTest = 200)]
    public void ValidateEncounter_ExceedingCeiling_IsFlagged(ValidationInput input)
    {
        var formula = new EncounterBudgetFormula
        {
            Type = input.BudgetType,
            DifficultyTiers = input.Tiers.ToList()
        };

        var result = Engine.ValidateEncounter(formula, input.Party, input.Creatures);
        var budgetResult = Engine.ComputeBudget(formula, input.Party);

        // Find the ceiling (highest tier budget)
        var ceilingBudget = budgetResult.TierBudgets.Values.Max();
        var totalCreatureCost = input.Creatures.Sum(c => (double)c.Cost);

        if (totalCreatureCost > ceilingBudget)
        {
            // Must be flagged
            Assert.True(result.ExceedsCeiling,
                $"Encounter with cost {totalCreatureCost} exceeding ceiling {ceilingBudget} should be flagged");
            Assert.False(result.IsValid);
            Assert.NotNull(result.Warning);
        }
        else
        {
            // Must not be flagged
            Assert.False(result.ExceedsCeiling,
                $"Encounter with cost {totalCreatureCost} within ceiling {ceilingBudget} should not be flagged");
            Assert.True(result.IsValid);
        }
    }
}
