using Aircane.Application.Validation;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for GameSystemDefinitionValidator correctness.
/// **Validates: Requirements 1.1, 1.2, 1.5, 9.4, 11.4**
///
/// Property 3: Game System Definition validation correctness
/// For any Game System Definition document, the validator SHALL accept all structurally valid
/// definitions and reject all structurally invalid definitions with error messages that identify
/// the specific invalid section or field path.
/// </summary>
public class GameSystemDefinitionValidationPropertyTests
{
    private readonly GameSystemDefinitionValidator _validator = new();

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty, non-whitespace string suitable for identifiers and names.
    /// </summary>
    private static Gen<string> NonEmptyAlphaString =>
        Gen.Elements(
            "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta",
            "test-system", "my-rpg", "custom-game", "homebrew-v1", "fantasy-core",
            "sci-fi-rules", "horror-lite", "narrative-engine", "dice-pool-system", "d20-variant");

    /// <summary>
    /// Generates a valid semantic version string.
    /// </summary>
    private static Gen<string> ValidVersionString =>
        from major in Gen.Choose(0, 9)
        from minor in Gen.Choose(0, 9)
        from patch in Gen.Choose(0, 9)
        select $"{major}.{minor}.{patch}";

    /// <summary>
    /// Generates a valid license string.
    /// </summary>
    private static Gen<string> ValidLicenseString =>
        Gen.Elements("built-in", "user-created", "community");

    /// <summary>
    /// Generates a valid DiceConventionType enum value.
    /// </summary>
    private static Gen<DiceConventionType> ValidDiceConventionType =>
        Gen.Elements(
            DiceConventionType.SingleDieModifier,
            DiceConventionType.DicePoolSuccess,
            DiceConventionType.FixedDiceThreshold,
            DiceConventionType.Fudge,
            DiceConventionType.StepDice,
            DiceConventionType.Percentile,
            DiceConventionType.Expression);

    /// <summary>
    /// Generates a valid ResolutionRuleType enum value.
    /// </summary>
    private static Gen<ResolutionRuleType> ValidResolutionRuleType =>
        Gen.Elements(
            ResolutionRuleType.TargetNumber,
            ResolutionRuleType.Opposed,
            ResolutionRuleType.DegreesOfSuccess,
            ResolutionRuleType.Margin,
            ResolutionRuleType.ThresholdBands);

    /// <summary>
    /// Generates a valid ActionEconomyType enum value.
    /// </summary>
    private static Gen<ActionEconomyType> ValidActionEconomyType =>
        Gen.Elements(
            ActionEconomyType.NamedSlots,
            ActionEconomyType.ActionPoints,
            ActionEconomyType.MultiActionPenalty,
            ActionEconomyType.Freeform);

    /// <summary>
    /// Generates a valid EncounterBudgetType enum value.
    /// </summary>
    private static Gen<EncounterBudgetType> ValidEncounterBudgetType =>
        Gen.Elements(
            EncounterBudgetType.XpBudget,
            EncounterBudgetType.CreatureLevel,
            EncounterBudgetType.ThreatRating,
            EncounterBudgetType.NarrativeTiers);

    /// <summary>
    /// Generates a valid CharacterFieldType enum value.
    /// </summary>
    private static Gen<CharacterFieldType> ValidCharacterFieldType =>
        Gen.Elements(
            CharacterFieldType.Text,
            CharacterFieldType.Number,
            CharacterFieldType.Boolean,
            CharacterFieldType.Enum,
            CharacterFieldType.DiceExpression,
            CharacterFieldType.List,
            CharacterFieldType.Repeating,
            CharacterFieldType.ResourcePool,
            CharacterFieldType.Calculated,
            CharacterFieldType.Grouped);

    /// <summary>
    /// Generates a list of valid DiceConventions with unique names.
    /// </summary>
    private static Gen<IReadOnlyList<DiceConvention>> ValidDiceConventions =>
        from count in Gen.Choose(1, 4)
        from types in Gen.ListOf(count, ValidDiceConventionType)
        let names = Enumerable.Range(0, count).Select(i => i == 0 ? "primary" : $"convention_{i}").ToList()
        select (IReadOnlyList<DiceConvention>)names.Zip(types, (name, type) =>
            new DiceConvention { Name = name, Type = type }).ToList();

    /// <summary>
    /// Generates valid ResolutionRules that reference existing convention names.
    /// </summary>
    private static Gen<IReadOnlyList<ResolutionRule>> ValidResolutionRules(IReadOnlyList<DiceConvention> conventions) =>
        from count in Gen.Choose(0, 3)
        from types in Gen.ListOf(count, ValidResolutionRuleType)
        let conventionNames = conventions.Select(c => c.Name).ToList()
        from rollIndices in Gen.ListOf(count, Gen.Choose(0, conventionNames.Count - 1))
        let ruleNames = Enumerable.Range(0, count).Select(i => $"rule_{i}").ToList()
        select (IReadOnlyList<ResolutionRule>)ruleNames
            .Select((name, i) => new ResolutionRule
            {
                Name = name,
                Type = types[i],
                Roll = conventionNames[rollIndices[i]]
            }).ToList();

    /// <summary>
    /// Generates a valid CharacterSchema (or null).
    /// </summary>
    private static Gen<CharacterSchema?> ValidCharacterSchemaOrNull =>
        Gen.OneOf(
            Gen.Constant<CharacterSchema?>(null),
            from sectionCount in Gen.Choose(1, 3)
            from fieldCounts in Gen.ListOf(sectionCount, Gen.Choose(1, 4))
            from fieldTypes in Gen.Sequence(fieldCounts.Select(fc => Gen.ListOf(fc, ValidCharacterFieldType)))
            select (CharacterSchema?)new CharacterSchema
            {
                Sections = Enumerable.Range(0, sectionCount).Select(si =>
                    new CharacterSchemaSection
                    {
                        Id = $"section_{si}",
                        Label = $"Section {si}",
                        Fields = Enumerable.Range(0, fieldCounts[si]).Select(fi =>
                            new CharacterSchemaField
                            {
                                Id = $"field_{si}_{fi}",
                                Type = fieldTypes.ElementAt(si)[fi]
                            }).ToList()
                    }).ToList()
            });

    /// <summary>
    /// Generates a valid ConditionSet (possibly empty).
    /// </summary>
    private static Gen<IReadOnlyList<ConditionDefinition>> ValidConditionSet =>
        from count in Gen.Choose(0, 3)
        from durationTypes in Gen.ListOf(count, Gen.Elements("rounds", "until_save", "until_action", "until_rest"))
        select (IReadOnlyList<ConditionDefinition>)Enumerable.Range(0, count).Select(i =>
            new ConditionDefinition
            {
                Name = $"Condition_{i}",
                DurationType = durationTypes[i]
            }).ToList();

    /// <summary>
    /// Generates a valid ActionEconomyDefinition (or null).
    /// </summary>
    private static Gen<ActionEconomyDefinition?> ValidActionEconomyOrNull =>
        Gen.OneOf(
            Gen.Constant<ActionEconomyDefinition?>(null),
            from type in ValidActionEconomyType
            select type switch
            {
                ActionEconomyType.NamedSlots => (ActionEconomyDefinition?)new ActionEconomyDefinition
                {
                    Type = type,
                    TurnStructure = new TurnStructure
                    {
                        Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
                    }
                },
                _ => new ActionEconomyDefinition { Type = type }
            });

    /// <summary>
    /// Generates a valid EncounterBudgetFormula (or null).
    /// </summary>
    private static Gen<EncounterBudgetFormula?> ValidEncounterBudgetOrNull =>
        Gen.OneOf(
            Gen.Constant<EncounterBudgetFormula?>(null),
            from type in ValidEncounterBudgetType
            from tierCount in Gen.Choose(1, 4)
            select (EncounterBudgetFormula?)new EncounterBudgetFormula
            {
                Type = type,
                DifficultyTiers = Enumerable.Range(0, tierCount).Select(i =>
                    new DifficultyTier { Name = $"Tier_{i}", Multiplier = 0.5 + (i * 0.5) }).ToList()
            });

    /// <summary>
    /// Generates a fully valid GameSystemDefinition.
    /// </summary>
    private static Gen<GameSystemDefinition> ValidGameSystemDefinition =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from conventions in ValidDiceConventions
        from rules in ValidResolutionRules(conventions)
        from schema in ValidCharacterSchemaOrNull
        from conditions in ValidConditionSet
        from actionEconomy in ValidActionEconomyOrNull
        from encounterBudget in ValidEncounterBudgetOrNull
        select new GameSystemDefinition(identifier, name, version, 1, license)
        {
            DiceConventions = conventions,
            ResolutionRules = rules,
            CharacterSchema = schema,
            ConditionSet = conditions,
            ActionEconomy = actionEconomy,
            EncounterBudget = encounterBudget
        };

    // ── Mutation strategies for generating invalid definitions ─────────────────

    public enum InvalidMutation
    {
        EmptyIdentifier,
        EmptyName,
        EmptyVersion,
        InvalidSchemaVersion,
        InvalidDiceConventionType,
        EmptyDiceConventionName,
        InvalidResolutionRuleType,
        EmptyResolutionRuleName,
        EmptyResolutionRuleRoll,
        ResolutionRuleReferencesNonExistentConvention,
        InvalidCharacterFieldType,
        EmptyCharacterSectionId,
        EmptyCharacterSectionLabel,
        EmptyCharacterFieldId,
        EmptyConditionName,
        EmptyConditionDurationType,
        InvalidActionEconomyType,
        NamedSlotsWithoutTurnStructure,
        NamedSlotsWithEmptySlots,
        InvalidEncounterBudgetType,
        EmptyEncounterBudgetTiers
    }

    /// <summary>
    /// Generates a mutation type.
    /// </summary>
    private static Gen<InvalidMutation> MutationType =>
        Gen.Elements(Enum.GetValues<InvalidMutation>());

    /// <summary>
    /// Applies a mutation to a valid definition to make it invalid.
    /// Returns the mutated definition and the expected property name prefix in the error.
    /// </summary>
    private static (GameSystemDefinition Definition, string ExpectedPropertyPrefix) ApplyMutation(
        GameSystemDefinition valid, InvalidMutation mutation)
    {
        // Clone by creating a new definition with same base properties
        var def = new GameSystemDefinition(valid.Identifier, valid.Name, valid.Version, valid.SchemaVersion, valid.License)
        {
            DiceConventions = valid.DiceConventions,
            ResolutionRules = valid.ResolutionRules,
            CharacterSchema = valid.CharacterSchema,
            ConditionSet = valid.ConditionSet,
            ActionEconomy = valid.ActionEconomy,
            EncounterBudget = valid.EncounterBudget
        };

        switch (mutation)
        {
            case InvalidMutation.EmptyIdentifier:
                def.Identifier = "";
                return (def, "Identifier");

            case InvalidMutation.EmptyName:
                def.Name = "";
                return (def, "Name");

            case InvalidMutation.EmptyVersion:
                def.Version = "";
                return (def, "Version");

            case InvalidMutation.InvalidSchemaVersion:
                def.SchemaVersion = 99;
                return (def, "SchemaVersion");

            case InvalidMutation.InvalidDiceConventionType:
                def.DiceConventions = [new DiceConvention { Name = "primary", Type = (DiceConventionType)999 }];
                def.ResolutionRules = [];
                return (def, "DiceConventions[0].Type");

            case InvalidMutation.EmptyDiceConventionName:
                def.DiceConventions = [new DiceConvention { Name = "", Type = DiceConventionType.SingleDieModifier }];
                def.ResolutionRules = [];
                return (def, "DiceConventions[0].Name");

            case InvalidMutation.InvalidResolutionRuleType:
                def.DiceConventions = [new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier }];
                def.ResolutionRules = [new ResolutionRule { Name = "check", Type = (ResolutionRuleType)999, Roll = "primary" }];
                return (def, "ResolutionRules[0].Type");

            case InvalidMutation.EmptyResolutionRuleName:
                def.DiceConventions = [new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier }];
                def.ResolutionRules = [new ResolutionRule { Name = "", Type = ResolutionRuleType.TargetNumber, Roll = "primary" }];
                return (def, "ResolutionRules[0].Name");

            case InvalidMutation.EmptyResolutionRuleRoll:
                def.DiceConventions = [new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier }];
                def.ResolutionRules = [new ResolutionRule { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "" }];
                return (def, "ResolutionRules[0].Roll");

            case InvalidMutation.ResolutionRuleReferencesNonExistentConvention:
                def.DiceConventions = [new DiceConvention { Name = "primary", Type = DiceConventionType.SingleDieModifier }];
                def.ResolutionRules = [new ResolutionRule { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "nonexistent_conv" }];
                return (def, "ResolutionRules[0].Roll");

            case InvalidMutation.InvalidCharacterFieldType:
                def.CharacterSchema = new CharacterSchema
                {
                    Sections = [new CharacterSchemaSection
                    {
                        Id = "basics", Label = "Basics",
                        Fields = [new CharacterSchemaField { Id = "name", Type = (CharacterFieldType)999 }]
                    }]
                };
                return (def, "CharacterSchema.Sections[0].Fields[0].Type");

            case InvalidMutation.EmptyCharacterSectionId:
                def.CharacterSchema = new CharacterSchema
                {
                    Sections = [new CharacterSchemaSection
                    {
                        Id = "", Label = "Basics",
                        Fields = [new CharacterSchemaField { Id = "name", Type = CharacterFieldType.Text }]
                    }]
                };
                return (def, "CharacterSchema.Sections[0].Id");

            case InvalidMutation.EmptyCharacterSectionLabel:
                def.CharacterSchema = new CharacterSchema
                {
                    Sections = [new CharacterSchemaSection
                    {
                        Id = "basics", Label = "",
                        Fields = [new CharacterSchemaField { Id = "name", Type = CharacterFieldType.Text }]
                    }]
                };
                return (def, "CharacterSchema.Sections[0].Label");

            case InvalidMutation.EmptyCharacterFieldId:
                def.CharacterSchema = new CharacterSchema
                {
                    Sections = [new CharacterSchemaSection
                    {
                        Id = "basics", Label = "Basics",
                        Fields = [new CharacterSchemaField { Id = "", Type = CharacterFieldType.Text }]
                    }]
                };
                return (def, "CharacterSchema.Sections[0].Fields[0].Id");

            case InvalidMutation.EmptyConditionName:
                def.ConditionSet = [new ConditionDefinition { Name = "", DurationType = "rounds" }];
                return (def, "ConditionSet[0].Name");

            case InvalidMutation.EmptyConditionDurationType:
                def.ConditionSet = [new ConditionDefinition { Name = "Poisoned", DurationType = "" }];
                return (def, "ConditionSet[0].DurationType");

            case InvalidMutation.InvalidActionEconomyType:
                def.ActionEconomy = new ActionEconomyDefinition { Type = (ActionEconomyType)999 };
                return (def, "ActionEconomy.Type");

            case InvalidMutation.NamedSlotsWithoutTurnStructure:
                def.ActionEconomy = new ActionEconomyDefinition { Type = ActionEconomyType.NamedSlots, TurnStructure = null };
                return (def, "ActionEconomy.TurnStructure");

            case InvalidMutation.NamedSlotsWithEmptySlots:
                def.ActionEconomy = new ActionEconomyDefinition
                {
                    Type = ActionEconomyType.NamedSlots,
                    TurnStructure = new TurnStructure { Slots = [] }
                };
                return (def, "ActionEconomy.TurnStructure.Slots");

            case InvalidMutation.InvalidEncounterBudgetType:
                def.EncounterBudget = new EncounterBudgetFormula
                {
                    Type = (EncounterBudgetType)999,
                    DifficultyTiers = [new DifficultyTier { Name = "Easy", Multiplier = 0.5 }]
                };
                return (def, "EncounterBudget.Type");

            case InvalidMutation.EmptyEncounterBudgetTiers:
                def.EncounterBudget = new EncounterBudgetFormula
                {
                    Type = EncounterBudgetType.XpBudget,
                    DifficultyTiers = []
                };
                return (def, "EncounterBudget.DifficultyTiers");

            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    // ── Arbitraries ───────────────────────────────────────────────────────────

    private static Arbitrary<GameSystemDefinition> ValidDefinitionArbitrary() =>
        Arb.From(ValidGameSystemDefinition);

    private static Arbitrary<(GameSystemDefinition, InvalidMutation)> InvalidDefinitionArbitrary() =>
        Arb.From(
            from valid in ValidGameSystemDefinition
            from mutation in MutationType
            select (valid, mutation));

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 3a: For any valid GameSystemDefinition (all required fields present, valid enum values,
    /// resolution rules reference existing conventions), the validator should produce no errors.
    /// **Validates: Requirements 1.1, 1.2, 1.5, 9.4, 11.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionValidationPropertyTests)], MaxTest = 200)]
    public void ValidDefinitions_ProduceNoValidationErrors(GameSystemDefinition definition)
    {
        var result = _validator.Validate(definition);

        Assert.True(result.IsValid,
            $"Expected valid definition to pass validation but got errors: " +
            $"{string.Join("; ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");
    }

    /// <summary>
    /// Property 3b: For any GameSystemDefinition with at least one invalid field (empty required string,
    /// invalid enum, resolution rule referencing non-existent convention), the validator should produce
    /// at least one error with a non-empty PropertyName that identifies the invalid field path.
    /// **Validates: Requirements 1.1, 1.2, 1.5, 9.4, 11.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionValidationPropertyTests)], MaxTest = 200)]
    public void InvalidDefinitions_ProduceAtLeastOneErrorWithFieldPath(
        (GameSystemDefinition Valid, InvalidMutation Mutation) input)
    {
        var (mutated, expectedPrefix) = ApplyMutation(input.Valid, input.Mutation);

        var result = _validator.Validate(mutated);

        Assert.False(result.IsValid,
            $"Expected invalid definition (mutation: {input.Mutation}) to fail validation but it passed.");

        Assert.Contains(result.Errors, e =>
            !string.IsNullOrEmpty(e.PropertyName) && e.PropertyName.StartsWith(expectedPrefix));
    }

    /// <summary>
    /// Property 3c: All validation errors should have non-empty PropertyName (field path format).
    /// For any invalid definition, every error in the result must have a non-empty PropertyName
    /// so that the caller can identify which field is problematic.
    /// **Validates: Requirements 1.1, 1.2, 1.5, 9.4, 11.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionValidationPropertyTests)], MaxTest = 200)]
    public void AllValidationErrors_HaveNonEmptyPropertyName(
        (GameSystemDefinition Valid, InvalidMutation Mutation) input)
    {
        var (mutated, _) = ApplyMutation(input.Valid, input.Mutation);

        var result = _validator.Validate(mutated);

        // Only check if there are errors (which there should be for invalid definitions)
        if (!result.IsValid)
        {
            Assert.All(result.Errors, error =>
                Assert.False(string.IsNullOrWhiteSpace(error.PropertyName),
                    $"Validation error has empty PropertyName. Error message: {error.ErrorMessage}"));
        }
    }

    // ── FsCheck Arbitrary registration ────────────────────────────────────────

    public static Arbitrary<GameSystemDefinition> GameSystemDefinitionArbitrary =>
        ValidDefinitionArbitrary();

    // FsCheck uses this to resolve (GameSystemDefinition, InvalidMutation) tuples
    public static Arbitrary<(GameSystemDefinition, InvalidMutation)> InvalidTupleArbitrary =>
        InvalidDefinitionArbitrary();
}
