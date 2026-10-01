using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for Definition export/import round-trip.
/// **Validates: Requirements 9.3**
///
/// Property 19: Definition export/import round-trip
/// For any stored Game System Definition, exporting it to JSON and then importing the
/// exported file SHALL produce a definition equivalent to the original.
///
/// Since the SystemRegistry requires a database, this tests at the serializer level:
/// PrintJson → ParseJson → compare (which is the core of Export → Import).
/// </summary>
public class ExportImportRoundTripPropertyTests
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

    private static Gen<string?> OptionalString =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("Some publisher", "Fantasy Games Inc", "Indie RPG Studio"));

    private static Gen<string?> OptionalGenre =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("fantasy", "sci-fi", "horror", "modern", "post-apocalyptic"));

    private static Gen<string?> OptionalDescription =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("A great RPG system", "Fast-paced action", "Narrative-focused gameplay"));

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
    /// Generates dice conventions following the printer's naming heuristic:
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

    private static Gen<IReadOnlyList<ResolutionRule>> RoundTripResolutionRules(IReadOnlyList<DiceConvention> conventions) =>
        conventions.Count == 0
            ? Gen.Constant<IReadOnlyList<ResolutionRule>>(new List<ResolutionRule>())
            : from count in Gen.Choose(1, 3)
              from types in Gen.ListOf(count, ValidResolutionRuleType)
              from rollIndices in Gen.ListOf(count, Gen.Choose(0, conventions.Count - 1))
              let ruleNames = new[] { "check", "attack", "save", "skill" }.Take(count).ToList()
              select (IReadOnlyList<ResolutionRule>)ruleNames
                  .Select((name, i) => new ResolutionRule
                  {
                      Name = name,
                      Type = types[i],
                      Roll = conventions[rollIndices[i]].Name,
                      Comparison = ">=",
                      TargetSource = "dc"
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
    /// Generates a valid GameSystemDefinition suitable for export/import round-trip testing.
    /// </summary>
    private static Gen<GameSystemDefinition> ExportableGameSystemDefinition =>
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
        Arb.From(ExportableGameSystemDefinition);

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 19a: For any valid GameSystemDefinition, exporting (PrintJson) and then
    /// importing (ParseJson) produces a successful parse result.
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_ProducesSuccessResult(GameSystemDefinition definition)
    {
        // Export: serialize to JSON (simulates ExportAsync)
        var exportedJson = _serializer.PrintJson(definition);

        // Import: parse from stream (simulates ImportAsync)
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess,
            $"Expected import to succeed after export but got errors: " +
            $"{string.Join("; ", importResult.Errors.Select(e => $"{e.FieldPath}: {e.Message}"))}");
    }

    /// <summary>
    /// Property 19b: For any valid GameSystemDefinition, exporting and importing preserves
    /// all metadata fields (Identifier, Name, Version, SchemaVersion, License, Publisher, Genre, Description, Tags).
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesMetadata(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        Assert.Equal(definition.Identifier, imported.Identifier);
        Assert.Equal(definition.Name, imported.Name);
        Assert.Equal(definition.Version, imported.Version);
        Assert.Equal(definition.SchemaVersion, imported.SchemaVersion);
        Assert.Equal(definition.License, imported.License);
        Assert.Equal(definition.Publisher, imported.Publisher);
        Assert.Equal(definition.Genre, imported.Genre);
        Assert.Equal(definition.Description, imported.Description);
        Assert.Equal(definition.Tags, imported.Tags);
    }

    /// <summary>
    /// Property 19c: For any valid GameSystemDefinition, exporting and importing preserves
    /// DiceConventions (count, names, types).
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesDiceConventions(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        Assert.Equal(definition.DiceConventions.Count, imported.DiceConventions.Count);

        for (var i = 0; i < definition.DiceConventions.Count; i++)
        {
            var original = definition.DiceConventions[i];
            var roundTripped = imported.DiceConventions.FirstOrDefault(c => c.Name == original.Name);

            Assert.NotNull(roundTripped);
            Assert.Equal(original.Type, roundTripped.Type);
            Assert.Equal(original.Name, roundTripped.Name);
        }
    }

    /// <summary>
    /// Property 19d: For any valid GameSystemDefinition, exporting and importing preserves
    /// ResolutionRules (count, names, types, roll references).
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesResolutionRules(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        Assert.Equal(definition.ResolutionRules.Count, imported.ResolutionRules.Count);

        for (var i = 0; i < definition.ResolutionRules.Count; i++)
        {
            var original = definition.ResolutionRules[i];
            var roundTripped = imported.ResolutionRules.FirstOrDefault(r => r.Name == original.Name);

            Assert.NotNull(roundTripped);
            Assert.Equal(original.Type, roundTripped.Type);
            Assert.Equal(original.Roll, roundTripped.Roll);
        }
    }

    /// <summary>
    /// Property 19e: For any valid GameSystemDefinition, exporting and importing preserves
    /// ConditionSet (count, names, duration types).
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesConditionSet(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        Assert.Equal(definition.ConditionSet.Count, imported.ConditionSet.Count);

        for (var i = 0; i < definition.ConditionSet.Count; i++)
        {
            Assert.Equal(definition.ConditionSet[i].Name, imported.ConditionSet[i].Name);
            Assert.Equal(definition.ConditionSet[i].DurationType, imported.ConditionSet[i].DurationType);
            Assert.Equal(definition.ConditionSet[i].Stackable, imported.ConditionSet[i].Stackable);
        }
    }

    /// <summary>
    /// Property 19f: For any valid GameSystemDefinition, exporting and importing preserves
    /// ActionEconomy type and structure.
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesActionEconomy(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        if (definition.ActionEconomy is null)
        {
            Assert.Null(imported.ActionEconomy);
        }
        else
        {
            Assert.NotNull(imported.ActionEconomy);
            Assert.Equal(definition.ActionEconomy.Type, imported.ActionEconomy.Type);

            if (definition.ActionEconomy.TurnStructure is not null)
            {
                Assert.NotNull(imported.ActionEconomy.TurnStructure);
                Assert.Equal(
                    definition.ActionEconomy.TurnStructure.Slots.Count,
                    imported.ActionEconomy.TurnStructure.Slots.Count);
            }
        }
    }

    /// <summary>
    /// Property 19g: For any valid GameSystemDefinition, exporting and importing preserves
    /// EncounterBudget type and difficulty tiers.
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesEncounterBudget(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        if (definition.EncounterBudget is null)
        {
            Assert.Null(imported.EncounterBudget);
        }
        else
        {
            Assert.NotNull(imported.EncounterBudget);
            Assert.Equal(definition.EncounterBudget.Type, imported.EncounterBudget.Type);
            Assert.Equal(
                definition.EncounterBudget.DifficultyTiers.Count,
                imported.EncounterBudget.DifficultyTiers.Count);

            for (var i = 0; i < definition.EncounterBudget.DifficultyTiers.Count; i++)
            {
                Assert.Equal(
                    definition.EncounterBudget.DifficultyTiers[i].Name,
                    imported.EncounterBudget.DifficultyTiers[i].Name);
                Assert.Equal(
                    definition.EncounterBudget.DifficultyTiers[i].Multiplier,
                    imported.EncounterBudget.DifficultyTiers[i].Multiplier);
            }
        }
    }

    /// <summary>
    /// Property 19h: For any valid GameSystemDefinition, exporting and importing preserves
    /// AiGuidance content.
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = [typeof(ExportImportRoundTripPropertyTests)], MaxTest = 200)]
    public void Export_ThenImport_PreservesAiGuidance(GameSystemDefinition definition)
    {
        var exportedJson = _serializer.PrintJson(definition);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exportedJson));
        var importResult = _serializer.ParseJson(stream);

        Assert.True(importResult.IsSuccess, "Import failed unexpectedly");
        var imported = importResult.Definition!;

        if (definition.AiGuidance is null)
        {
            Assert.Null(imported.AiGuidance);
        }
        else
        {
            Assert.NotNull(imported.AiGuidance);
            Assert.Equal(definition.AiGuidance.SystemPromptNotes, imported.AiGuidance.SystemPromptNotes);
            Assert.Equal(definition.AiGuidance.ToneGuidance, imported.AiGuidance.ToneGuidance);
            Assert.Equal(definition.AiGuidance.MechanicalNotes, imported.AiGuidance.MechanicalNotes);
            Assert.Equal(definition.AiGuidance.RollFormatExample, imported.AiGuidance.RollFormatExample);
            Assert.Equal(definition.AiGuidance.CommonMistakes, imported.AiGuidance.CommonMistakes);
        }
    }
}
