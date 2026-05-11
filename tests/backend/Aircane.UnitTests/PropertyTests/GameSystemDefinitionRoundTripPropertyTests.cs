using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for Game System Definition round-trip (parse-print-parse).
/// **Validates: Requirements 13.4**
///
/// Property 1: Game System Definition round-trip (parse-print-parse)
/// For any valid Game System Definition object, serializing it to JSON and then parsing
/// the result back SHALL produce an object equivalent to the original.
/// </summary>
public class GameSystemDefinitionRoundTripPropertyTests
{
    private readonly GameSystemDefinitionSerializer _serializer = new();

    // ── Generators ────────────────────────────────────────────────────────────

    private static Gen<string> NonEmptyAlphaString =>
        Gen.Elements(
            "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta",
            "test-system", "my-rpg", "custom-game", "homebrew-v1", "fantasy-core",
            "sci-fi-rules", "horror-lite", "narrative-engine", "dice-pool-system", "d20-variant");

    private static Gen<string> ValidVersionString =>
        from major in Gen.Choose(0, 9)
        from minor in Gen.Choose(0, 9)
        from patch in Gen.Choose(0, 9)
        select $"{major}.{minor}.{patch}";

    private static Gen<string> ValidLicenseString =>
        Gen.Elements("built-in", "user-created", "community");

    private static Gen<string> OptionalString =>
        Gen.OneOf(
            Gen.Constant<string>(null!),
            Gen.Elements("Some publisher", "Fantasy Games Inc", "Indie RPG Studio"));

    private static Gen<string> OptionalGenre =>
        Gen.OneOf(
            Gen.Constant<string>(null!),
            Gen.Elements("fantasy", "sci-fi", "horror", "modern", "post-apocalyptic"));

    private static Gen<string> OptionalDescription =>
        Gen.OneOf(
            Gen.Constant<string>(null!),
            Gen.Elements("A great RPG system", "Fast-paced action", "Narrative-focused gameplay"));

    private static Gen<IReadOnlyList<string>> TagsList =>
        from count in Gen.Choose(0, 4)
        from tags in Gen.ListOf(count, Gen.Elements("d20", "fantasy", "levels", "narrative", "horror", "sci-fi"))
        select (IReadOnlyList<string>)tags.Distinct().ToList();

    private static Gen<DiceConventionType> ValidDiceConventionType =>
        Gen.Elements(
            DiceConventionType.SingleDieModifier,
            DiceConventionType.DicePoolSuccess,
            DiceConventionType.FixedDiceThreshold,
            DiceConventionType.Fudge,
            DiceConventionType.StepDice,
            DiceConventionType.Percentile,
            DiceConventionType.Expression);

    private static Gen<ResolutionRuleType> ValidResolutionRuleType =>
        Gen.Elements(
            ResolutionRuleType.TargetNumber,
            ResolutionRuleType.Opposed,
            ResolutionRuleType.DegreesOfSuccess,
            ResolutionRuleType.Margin,
            ResolutionRuleType.ThresholdBands);

    private static Gen<ActionEconomyType> ValidActionEconomyType =>
        Gen.Elements(
            ActionEconomyType.NamedSlots,
            ActionEconomyType.ActionPoints,
            ActionEconomyType.MultiActionPenalty,
            ActionEconomyType.Freeform);

    private static Gen<EncounterBudgetType> ValidEncounterBudgetType =>
        Gen.Elements(
            EncounterBudgetType.XpBudget,
            EncounterBudgetType.CreatureLevel,
            EncounterBudgetType.ThreatRating,
            EncounterBudgetType.NarrativeTiers);

    /// <summary>
    /// Generates dice conventions with names that follow the printer's heuristic:
    /// - "primary" for the primary convention
    /// - Simple names without underscores for named top-level keys
    /// - Names with underscores for custom array items
    /// </summary>
    private static Gen<IReadOnlyList<DiceConvention>> RoundTripDiceConventions =>
        from hasPrimary in Gen.Elements(true, false)
        from namedCount in Gen.Choose(0, 2)
        from customCount in Gen.Choose(0, 2)
        from types in Gen.ListOf(
            (hasPrimary ? 1 : 0) + namedCount + customCount,
            ValidDiceConventionType)
        let namedNames = new[] { "damage", "healing", "initiative" }.Take(namedCount).ToList()
        let customNames = new[] { "hit_dice", "wild_magic", "sneak_attack" }.Take(customCount).ToList()
        select BuildDiceConventions(hasPrimary, namedNames, customNames, types.ToList());

    private static IReadOnlyList<DiceConvention> BuildDiceConventions(
        bool hasPrimary,
        List<string> namedNames,
        List<string> customNames,
        IList<DiceConventionType> types)
    {
        var conventions = new List<DiceConvention>();
        var typeIndex = 0;

        if (hasPrimary)
        {
            conventions.Add(new DiceConvention
            {
                Name = "primary",
                Type = types[typeIndex++],
                Die = "d20",
                Description = "Primary roll"
            });
        }

        foreach (var name in namedNames)
        {
            conventions.Add(new DiceConvention
            {
                Name = name,
                Type = types[typeIndex++],
                Description = $"{name} convention"
            });
        }

        foreach (var name in customNames)
        {
            conventions.Add(new DiceConvention
            {
                Name = name,
                Type = types[typeIndex++],
                Description = $"{name} convention"
            });
        }

        return conventions;
    }

    /// <summary>
    /// Generates resolution rules that reference existing convention names.
    /// </summary>
    private static Gen<IReadOnlyList<ResolutionRule>> RoundTripResolutionRules(IReadOnlyList<DiceConvention> conventions) =>
        conventions.Count == 0
            ? Gen.Constant<IReadOnlyList<ResolutionRule>>(new List<ResolutionRule>())
            : from count in Gen.Choose(0, 3)
              from types in Gen.ListOf(count, ValidResolutionRuleType)
              from rollIndices in Gen.ListOf(count, Gen.Choose(0, conventions.Count - 1))
              let ruleNames = new[] { "check", "attack", "save", "skill" }.Take(count).ToList()
              select (IReadOnlyList<ResolutionRule>)ruleNames
                  .Select((name, i) => new ResolutionRule
                  {
                      Name = name,
                      Type = types[i],
                      Roll = conventions[rollIndices[i]].Name
                  }).ToList();

    private static Gen<IReadOnlyList<ConditionDefinition>> ValidConditionSet =>
        from count in Gen.Choose(0, 3)
        from durationTypes in Gen.ListOf(count, Gen.Elements("rounds", "until_save", "until_action", "until_rest"))
        select (IReadOnlyList<ConditionDefinition>)Enumerable.Range(0, count).Select(i =>
            new ConditionDefinition
            {
                Name = $"Condition{i}",
                Description = $"Description for condition {i}",
                DurationType = durationTypes[i],
                Stackable = i % 2 == 0,
                Effects = new List<ConditionEffect>
                {
                    new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" }
                }
            }).ToList();

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
                        Slots =
                        [
                            new ActionSlot { Name = "action", Label = "Action", Count = 1 },
                            new ActionSlot { Name = "bonus", Label = "Bonus Action", Count = 1 }
                        ]
                    }
                },
                ActionEconomyType.ActionPoints => new ActionEconomyDefinition
                {
                    Type = type,
                    PointsPerTurn = 3
                },
                ActionEconomyType.MultiActionPenalty => new ActionEconomyDefinition
                {
                    Type = type,
                    PenaltyIncrement = 5,
                    MaxActions = 3
                },
                _ => new ActionEconomyDefinition { Type = type }
            });

    private static Gen<EncounterBudgetFormula?> ValidEncounterBudgetOrNull =>
        Gen.OneOf(
            Gen.Constant<EncounterBudgetFormula?>(null),
            from type in ValidEncounterBudgetType
            from tierCount in Gen.Choose(1, 4)
            select (EncounterBudgetFormula?)new EncounterBudgetFormula
            {
                Type = type,
                DifficultyTiers = Enumerable.Range(0, tierCount).Select(i =>
                    new DifficultyTier { Name = $"Tier{i}", Multiplier = 0.5 + (i * 0.5) }).ToList()
            });

    private static Gen<AiGuidance?> ValidAiGuidanceOrNull =>
        Gen.OneOf(
            Gen.Constant<AiGuidance?>(null),
            Gen.Constant<AiGuidance?>(new AiGuidance
            {
                SystemPromptNotes = "This is a test system",
                ToneGuidance = "Heroic fantasy",
                MechanicalNotes = "Use d20 for checks",
                CommonMistakes = ["Do not use degrees of success"],
                RollFormatExample = "1d20+modifier"
            }));

    /// <summary>
    /// Generates a valid GameSystemDefinition suitable for round-trip testing.
    /// Uses naming conventions that match the printer's heuristic for custom vs named dice conventions.
    /// </summary>
    private static Gen<GameSystemDefinition> RoundTripGameSystemDefinition =>
        from identifier in NonEmptyAlphaString
        from name in NonEmptyAlphaString
        from version in ValidVersionString
        from license in ValidLicenseString
        from publisher in OptionalString
        from genre in OptionalGenre
        from description in OptionalDescription
        from tags in TagsList
        from conventions in RoundTripDiceConventions
        from rules in RoundTripResolutionRules(conventions)
        from conditions in ValidConditionSet
        from actionEconomy in ValidActionEconomyOrNull
        from encounterBudget in ValidEncounterBudgetOrNull
        from aiGuidance in ValidAiGuidanceOrNull
        select new GameSystemDefinition(identifier, name, version, 1, license, publisher, genre, description)
        {
            Tags = tags,
            DiceConventions = conventions,
            ResolutionRules = rules,
            ConditionSet = conditions,
            ActionEconomy = actionEconomy,
            EncounterBudget = encounterBudget,
            AiGuidance = aiGuidance
        };

    // ── Arbitraries ───────────────────────────────────────────────────────────

    public static Arbitrary<GameSystemDefinition> GameSystemDefinitionArbitrary =>
        Arb.From(RoundTripGameSystemDefinition);

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1a: For any valid GameSystemDefinition, PrintJson → ParseJson produces IsSuccess == true.
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_ProducesSuccessResult(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess,
            $"Expected ParseJson to succeed after PrintJson but got errors: " +
            $"{string.Join("; ", result.Errors.Select(e => $"{e.FieldPath}: {e.Message}"))}");
    }

    /// <summary>
    /// Property 1b: For any valid GameSystemDefinition, PrintJson → ParseJson produces a definition
    /// with equivalent metadata (Identifier, Name, Version, SchemaVersion, License, Publisher, Genre, Description, Tags).
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_PreservesMetadata(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, "Parse failed unexpectedly");
        var parsed = result.Definition!;

        Assert.Equal(definition.Identifier, parsed.Identifier);
        Assert.Equal(definition.Name, parsed.Name);
        Assert.Equal(definition.Version, parsed.Version);
        Assert.Equal(definition.SchemaVersion, parsed.SchemaVersion);
        Assert.Equal(definition.License, parsed.License);
        Assert.Equal(definition.Publisher, parsed.Publisher);
        Assert.Equal(definition.Genre, parsed.Genre);
        Assert.Equal(definition.Description, parsed.Description);
        Assert.Equal(definition.Tags, parsed.Tags);
    }

    /// <summary>
    /// Property 1c: For any valid GameSystemDefinition, PrintJson → ParseJson preserves
    /// DiceConventions (count, types, names).
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_PreservesDiceConventions(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, "Parse failed unexpectedly");
        var parsed = result.Definition!;

        Assert.Equal(definition.DiceConventions.Count, parsed.DiceConventions.Count);

        for (var i = 0; i < definition.DiceConventions.Count; i++)
        {
            var original = definition.DiceConventions[i];
            var roundTripped = parsed.DiceConventions.FirstOrDefault(c => c.Name == original.Name);

            Assert.NotNull(roundTripped);
            Assert.Equal(original.Type, roundTripped.Type);
            Assert.Equal(original.Name, roundTripped.Name);
        }
    }

    /// <summary>
    /// Property 1d: For any valid GameSystemDefinition, PrintJson → ParseJson preserves
    /// ResolutionRules (count, types, names, rolls).
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_PreservesResolutionRules(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, "Parse failed unexpectedly");
        var parsed = result.Definition!;

        Assert.Equal(definition.ResolutionRules.Count, parsed.ResolutionRules.Count);

        for (var i = 0; i < definition.ResolutionRules.Count; i++)
        {
            var original = definition.ResolutionRules[i];
            var roundTripped = parsed.ResolutionRules.FirstOrDefault(r => r.Name == original.Name);

            Assert.NotNull(roundTripped);
            Assert.Equal(original.Type, roundTripped.Type);
            Assert.Equal(original.Name, roundTripped.Name);
            Assert.Equal(original.Roll, roundTripped.Roll);
        }
    }

    /// <summary>
    /// Property 1e: For any valid GameSystemDefinition, PrintJson → ParseJson preserves
    /// ConditionSet (count, names).
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_PreservesConditionSet(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, "Parse failed unexpectedly");
        var parsed = result.Definition!;

        Assert.Equal(definition.ConditionSet.Count, parsed.ConditionSet.Count);

        for (var i = 0; i < definition.ConditionSet.Count; i++)
        {
            Assert.Equal(definition.ConditionSet[i].Name, parsed.ConditionSet[i].Name);
        }
    }

    /// <summary>
    /// Property 1f: For any valid GameSystemDefinition, PrintJson → ParseJson preserves
    /// ActionEconomy type.
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_PreservesActionEconomy(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, "Parse failed unexpectedly");
        var parsed = result.Definition!;

        if (definition.ActionEconomy is null)
        {
            Assert.Null(parsed.ActionEconomy);
        }
        else
        {
            Assert.NotNull(parsed.ActionEconomy);
            Assert.Equal(definition.ActionEconomy.Type, parsed.ActionEconomy.Type);
        }
    }

    /// <summary>
    /// Property 1g: For any valid GameSystemDefinition, PrintJson → ParseJson preserves
    /// EncounterBudget type and tier count.
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = [typeof(GameSystemDefinitionRoundTripPropertyTests)], MaxTest = 200)]
    public void PrintJson_ThenParseJson_PreservesEncounterBudget(GameSystemDefinition definition)
    {
        var json = _serializer.PrintJson(definition);
        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess, "Parse failed unexpectedly");
        var parsed = result.Definition!;

        if (definition.EncounterBudget is null)
        {
            Assert.Null(parsed.EncounterBudget);
        }
        else
        {
            Assert.NotNull(parsed.EncounterBudget);
            Assert.Equal(definition.EncounterBudget.Type, parsed.EncounterBudget.Type);
            Assert.Equal(
                definition.EncounterBudget.DifficultyTiers.Count,
                parsed.EncounterBudget.DifficultyTiers.Count);
        }
    }
}
