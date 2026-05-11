using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for GameSystemDefaultsApplicator.
/// **Validates: Requirements 1.4**
///
/// Property 4: Default application for omitted optional sections
/// For any Game System Definition that omits one or more optional sections (conditionSet,
/// actionEconomy, encounterBudget, aiGuidance), the loaded definition SHALL have sensible
/// defaults applied for each missing section (freeform conditions, freeform turns, no automated
/// balancing, generic AI guidance).
/// </summary>
public class GameSystemDefaultsPropertyTests
{
    private readonly GameSystemDefaultsApplicator _applicator = new();

    // ── Generators ────────────────────────────────────────────────────────────

    private static Gen<string> NonEmptyAlphaString =>
        Gen.Elements(
            "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta",
            "test-system", "my-rpg", "custom-game", "homebrew-v1", "fantasy-core");

    private static Gen<string> ValidVersionString =>
        from major in Gen.Choose(0, 9)
        from minor in Gen.Choose(0, 9)
        from patch in Gen.Choose(0, 9)
        select $"{major}.{minor}.{patch}";

    private static Gen<string> ValidLicenseString =>
        Gen.Elements("built-in", "user-created", "community");

    private static Gen<DiceConventionType> ValidDiceConventionType =>
        Gen.Elements(
            DiceConventionType.SingleDieModifier,
            DiceConventionType.DicePoolSuccess,
            DiceConventionType.FixedDiceThreshold,
            DiceConventionType.Fudge,
            DiceConventionType.StepDice,
            DiceConventionType.Percentile,
            DiceConventionType.Expression);

    private static Gen<IReadOnlyList<DiceConvention>> ValidDiceConventions =>
        from count in Gen.Choose(1, 3)
        from types in Gen.ListOf(count, ValidDiceConventionType)
        let names = Enumerable.Range(0, count).Select(i => i == 0 ? "primary" : $"convention_{i}").ToList()
        select (IReadOnlyList<DiceConvention>)names.Zip(types, (name, type) =>
            new DiceConvention { Name = name, Type = type }).ToList();

    private static Gen<ActionEconomyDefinition> NonNullActionEconomy =>
        from type in Gen.Elements(
            ActionEconomyType.NamedSlots,
            ActionEconomyType.ActionPoints,
            ActionEconomyType.MultiActionPenalty,
            ActionEconomyType.Freeform)
        select type switch
        {
            ActionEconomyType.NamedSlots => new ActionEconomyDefinition
            {
                Type = type,
                TurnStructure = new TurnStructure
                {
                    Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
                }
            },
            ActionEconomyType.ActionPoints => new ActionEconomyDefinition
            {
                Type = type,
                PointsPerTurn = 6
            },
            _ => new ActionEconomyDefinition { Type = type }
        };

    private static Gen<AiGuidance> NonNullAiGuidance =>
        from notes in Gen.Elements("Use d20 mechanics", "Dice pool system", "PbtA moves")
        from tone in Gen.Elements("Dark horror", "High fantasy", "Gritty noir")
        select new AiGuidance
        {
            SystemPromptNotes = notes,
            ToneGuidance = tone,
            MechanicalNotes = "Follow the rules as written.",
            CommonMistakes = ["Don't assume d20 mechanics."]
        };

    private static Gen<EncounterBudgetFormula> NonNullEncounterBudget =>
        from type in Gen.Elements(
            EncounterBudgetType.XpBudget,
            EncounterBudgetType.CreatureLevel,
            EncounterBudgetType.ThreatRating,
            EncounterBudgetType.NarrativeTiers)
        select new EncounterBudgetFormula
        {
            Type = type,
            DifficultyTiers = [new DifficultyTier { Name = "Easy", Multiplier = 0.5 }]
        };

    private static Gen<IReadOnlyList<ConditionDefinition>> NonEmptyConditionSet =>
        from count in Gen.Choose(1, 4)
        select (IReadOnlyList<ConditionDefinition>)Enumerable.Range(0, count).Select(i =>
            new ConditionDefinition
            {
                Name = $"Condition_{i}",
                DurationType = "rounds"
            }).ToList();

    /// <summary>
    /// Generates a GameSystemDefinition with null ActionEconomy.
    /// </summary>
    private static Gen<GameSystemDefinition> DefinitionWithNullActionEconomy =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            ActionEconomy = null
        };

    /// <summary>
    /// Generates a GameSystemDefinition with null AiGuidance.
    /// </summary>
    private static Gen<GameSystemDefinition> DefinitionWithNullAiGuidance =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            AiGuidance = null
        };

    /// <summary>
    /// Generates a GameSystemDefinition with null EncounterBudget.
    /// </summary>
    private static Gen<GameSystemDefinition> DefinitionWithNullEncounterBudget =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            EncounterBudget = null
        };

    /// <summary>
    /// Generates a GameSystemDefinition with empty ConditionSet.
    /// </summary>
    private static Gen<GameSystemDefinition> DefinitionWithEmptyConditionSet =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            ConditionSet = []
        };

    /// <summary>
    /// Generates a GameSystemDefinition with all optional sections populated (non-null).
    /// </summary>
    private static Gen<GameSystemDefinition> DefinitionWithAllOptionalSections =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        from actionEconomy in NonNullActionEconomy
        from aiGuidance in NonNullAiGuidance
        from encounterBudget in NonNullEncounterBudget
        from conditions in NonEmptyConditionSet
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            ActionEconomy = actionEconomy,
            AiGuidance = aiGuidance,
            EncounterBudget = encounterBudget,
            ConditionSet = conditions
        };

    /// <summary>
    /// Generates a GameSystemDefinition with various combinations of null/non-null optional sections.
    /// </summary>
    private static Gen<GameSystemDefinition> DefinitionWithMixedOptionalSections =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        from hasActionEconomy in Arb.Generate<bool>()
        from hasAiGuidance in Arb.Generate<bool>()
        from hasEncounterBudget in Arb.Generate<bool>()
        from hasConditions in Arb.Generate<bool>()
        from actionEconomy in NonNullActionEconomy
        from aiGuidance in NonNullAiGuidance
        from encounterBudget in NonNullEncounterBudget
        from conditions in NonEmptyConditionSet
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            ActionEconomy = hasActionEconomy ? actionEconomy : null,
            AiGuidance = hasAiGuidance ? aiGuidance : null,
            EncounterBudget = hasEncounterBudget ? encounterBudget : null,
            ConditionSet = hasConditions ? conditions : []
        };

    // ── Arbitraries ───────────────────────────────────────────────────────────

    public static Arbitrary<GameSystemDefinition> GameSystemDefinitionArbitrary =>
        Arb.From(DefinitionWithMixedOptionalSections);

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 4a: For any GameSystemDefinition with null ActionEconomy, after applying defaults,
    /// ActionEconomy.Type == Freeform.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property NullActionEconomy_DefaultsToFreeform()
    {
        return Prop.ForAll(
            Arb.From(DefinitionWithNullActionEconomy),
            definition =>
            {
                var result = _applicator.ApplyDefaults(definition);

                Assert.NotNull(result.ActionEconomy);
                Assert.Equal(ActionEconomyType.Freeform, result.ActionEconomy!.Type);
            });
    }

    /// <summary>
    /// Property 4b: For any GameSystemDefinition with null AiGuidance, after applying defaults,
    /// AiGuidance is non-null with generic guidance text.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property NullAiGuidance_DefaultsToGenericGuidance()
    {
        return Prop.ForAll(
            Arb.From(DefinitionWithNullAiGuidance),
            definition =>
            {
                var result = _applicator.ApplyDefaults(definition);

                Assert.NotNull(result.AiGuidance);
                Assert.False(string.IsNullOrWhiteSpace(result.AiGuidance!.SystemPromptNotes),
                    "Default AiGuidance should have non-empty SystemPromptNotes");
                Assert.False(string.IsNullOrWhiteSpace(result.AiGuidance.ToneGuidance),
                    "Default AiGuidance should have non-empty ToneGuidance");
                Assert.False(string.IsNullOrWhiteSpace(result.AiGuidance.MechanicalNotes),
                    "Default AiGuidance should have non-empty MechanicalNotes");
                Assert.NotNull(result.AiGuidance.CommonMistakes);
                Assert.NotEmpty(result.AiGuidance.CommonMistakes);
            });
    }

    /// <summary>
    /// Property 4c: For any GameSystemDefinition with null EncounterBudget, after applying defaults,
    /// EncounterBudget remains null (no automated balancing).
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property NullEncounterBudget_RemainsNull()
    {
        return Prop.ForAll(
            Arb.From(DefinitionWithNullEncounterBudget),
            definition =>
            {
                var result = _applicator.ApplyDefaults(definition);

                Assert.Null(result.EncounterBudget);
            });
    }

    /// <summary>
    /// Property 4d: For any GameSystemDefinition with empty ConditionSet, after applying defaults,
    /// ConditionSet remains empty (freeform conditions are allowed).
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property EmptyConditionSet_RemainsEmpty()
    {
        return Prop.ForAll(
            Arb.From(DefinitionWithEmptyConditionSet),
            definition =>
            {
                var result = _applicator.ApplyDefaults(definition);

                Assert.NotNull(result.ConditionSet);
                Assert.Empty(result.ConditionSet);
            });
    }

    /// <summary>
    /// Property 4e: For any GameSystemDefinition with non-null optional sections, after applying
    /// defaults, those sections are preserved unchanged.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property NonNullOptionalSections_PreservedUnchanged()
    {
        return Prop.ForAll(
            Arb.From(DefinitionWithAllOptionalSections),
            definition =>
            {
                var result = _applicator.ApplyDefaults(definition);

                // ActionEconomy should be the same instance (records use structural equality)
                Assert.Equal(definition.ActionEconomy, result.ActionEconomy);
                Assert.Equal(definition.ActionEconomy!.Type, result.ActionEconomy!.Type);

                // AiGuidance should be preserved
                Assert.Equal(definition.AiGuidance, result.AiGuidance);
                Assert.Equal(definition.AiGuidance!.SystemPromptNotes, result.AiGuidance!.SystemPromptNotes);
                Assert.Equal(definition.AiGuidance.ToneGuidance, result.AiGuidance.ToneGuidance);

                // EncounterBudget should be preserved
                Assert.Equal(definition.EncounterBudget, result.EncounterBudget);
                Assert.Equal(definition.EncounterBudget!.Type, result.EncounterBudget!.Type);

                // ConditionSet should be preserved
                Assert.Equal(definition.ConditionSet.Count, result.ConditionSet.Count);
                for (int i = 0; i < definition.ConditionSet.Count; i++)
                {
                    Assert.Equal(definition.ConditionSet[i].Name, result.ConditionSet[i].Name);
                }
            });
    }

    /// <summary>
    /// Property 4f: ApplyDefaults does not mutate the original definition (returns a new instance).
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefaultsPropertyTests)], MaxTest = 200)]
    public void ApplyDefaults_DoesNotMutateOriginal(GameSystemDefinition definition)
    {
        // Capture original state
        var originalActionEconomy = definition.ActionEconomy;
        var originalAiGuidance = definition.AiGuidance;
        var originalEncounterBudget = definition.EncounterBudget;
        var originalConditionSet = definition.ConditionSet;
        var originalIdentifier = definition.Identifier;
        var originalName = definition.Name;

        var result = _applicator.ApplyDefaults(definition);

        // The result should be a different object reference
        Assert.NotSame(definition, result);

        // Original should be unchanged
        Assert.Equal(originalActionEconomy, definition.ActionEconomy);
        Assert.Equal(originalAiGuidance, definition.AiGuidance);
        Assert.Equal(originalEncounterBudget, definition.EncounterBudget);
        Assert.Same(originalConditionSet, definition.ConditionSet);
        Assert.Equal(originalIdentifier, definition.Identifier);
        Assert.Equal(originalName, definition.Name);
    }
}
