using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class GameSystemDefaultsApplicatorTests
{
    private readonly GameSystemDefaultsApplicator _sut = new();

    private static GameSystemDefinition CreateMinimalDefinition(
        ActionEconomyDefinition? actionEconomy = null,
        EncounterBudgetFormula? encounterBudget = null,
        AiGuidance? aiGuidance = null,
        IReadOnlyList<ConditionDefinition>? conditionSet = null)
    {
        var def = new GameSystemDefinition(
            identifier: "test-system",
            name: "Test System",
            version: "1.0.0",
            schemaVersion: 1,
            license: "user-created");

        def.ActionEconomy = actionEconomy;
        def.EncounterBudget = encounterBudget;
        def.AiGuidance = aiGuidance;
        if (conditionSet is not null)
            def.ConditionSet = conditionSet;

        return def;
    }

    [Fact]
    public void ApplyDefaults_WhenActionEconomyIsNull_AppliesFreeformDefault()
    {
        var definition = CreateMinimalDefinition(actionEconomy: null);

        var result = _sut.ApplyDefaults(definition);

        Assert.NotNull(result.ActionEconomy);
        Assert.Equal(ActionEconomyType.Freeform, result.ActionEconomy.Type);
        Assert.Null(result.ActionEconomy.TurnStructure);
        Assert.Null(result.ActionEconomy.PointsPerTurn);
        Assert.Null(result.ActionEconomy.PenaltyIncrement);
        Assert.Null(result.ActionEconomy.MaxActions);
    }

    [Fact]
    public void ApplyDefaults_WhenActionEconomyIsProvided_PreservesExisting()
    {
        var existingEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots = [new ActionSlot { Name = "action", Label = "Action", Count = 1 }]
            }
        };
        var definition = CreateMinimalDefinition(actionEconomy: existingEconomy);

        var result = _sut.ApplyDefaults(definition);

        Assert.Same(existingEconomy, result.ActionEconomy);
    }

    [Fact]
    public void ApplyDefaults_WhenAiGuidanceIsNull_AppliesGenericGuidance()
    {
        var definition = CreateMinimalDefinition(aiGuidance: null);

        var result = _sut.ApplyDefaults(definition);

        Assert.NotNull(result.AiGuidance);
        Assert.Equal(
            "This is a tabletop RPG. Follow the game system's rules as described in the definition.",
            result.AiGuidance.SystemPromptNotes);
        Assert.Equal(
            "Adapt tone to the genre and setting of the game system.",
            result.AiGuidance.ToneGuidance);
        Assert.Equal(
            "Use the dice conventions and resolution rules defined in the game system definition.",
            result.AiGuidance.MechanicalNotes);
        Assert.Single(result.AiGuidance.CommonMistakes);
        Assert.Equal(
            "Do not assume d20-based mechanics unless the dice convention specifies it.",
            result.AiGuidance.CommonMistakes[0]);
        Assert.Null(result.AiGuidance.RollFormatExample);
    }

    [Fact]
    public void ApplyDefaults_WhenAiGuidanceIsProvided_PreservesExisting()
    {
        var existingGuidance = new AiGuidance
        {
            SystemPromptNotes = "Custom notes",
            ToneGuidance = "Dark and gritty",
            MechanicalNotes = "Use d100",
            CommonMistakes = ["Don't use hit points"],
            RollFormatExample = "1d100"
        };
        var definition = CreateMinimalDefinition(aiGuidance: existingGuidance);

        var result = _sut.ApplyDefaults(definition);

        Assert.Same(existingGuidance, result.AiGuidance);
    }

    [Fact]
    public void ApplyDefaults_WhenEncounterBudgetIsNull_LeavesAsNull()
    {
        var definition = CreateMinimalDefinition(encounterBudget: null);

        var result = _sut.ApplyDefaults(definition);

        Assert.Null(result.EncounterBudget);
    }

    [Fact]
    public void ApplyDefaults_WhenEncounterBudgetIsProvided_PreservesExisting()
    {
        var existingBudget = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers = [new DifficultyTier { Name = "Easy", Multiplier = 0.5 }],
            Formula = "sum(xp)",
            CreatureCostField = "xp"
        };
        var definition = CreateMinimalDefinition(encounterBudget: existingBudget);

        var result = _sut.ApplyDefaults(definition);

        Assert.Same(existingBudget, result.EncounterBudget);
    }

    [Fact]
    public void ApplyDefaults_WhenConditionSetIsEmpty_LeavesAsEmptyList()
    {
        var definition = CreateMinimalDefinition();
        // Default ConditionSet is already an empty list

        var result = _sut.ApplyDefaults(definition);

        Assert.NotNull(result.ConditionSet);
        Assert.Empty(result.ConditionSet);
    }

    [Fact]
    public void ApplyDefaults_WhenConditionSetHasItems_PreservesExisting()
    {
        var conditions = new List<ConditionDefinition>
        {
            new() { Name = "Poisoned", Description = "Disadvantage on attacks" }
        };
        var definition = CreateMinimalDefinition(conditionSet: conditions);

        var result = _sut.ApplyDefaults(definition);

        Assert.Single(result.ConditionSet);
        Assert.Equal("Poisoned", result.ConditionSet[0].Name);
    }

    [Fact]
    public void ApplyDefaults_DoesNotMutateOriginalDefinition()
    {
        var definition = CreateMinimalDefinition(
            actionEconomy: null,
            aiGuidance: null);

        var result = _sut.ApplyDefaults(definition);

        // Original should remain null
        Assert.Null(definition.ActionEconomy);
        Assert.Null(definition.AiGuidance);

        // Result should have defaults
        Assert.NotNull(result.ActionEconomy);
        Assert.NotNull(result.AiGuidance);
    }

    [Fact]
    public void ApplyDefaults_PreservesMetadataFields()
    {
        var definition = new GameSystemDefinition(
            identifier: "my-system",
            name: "My System",
            version: "2.1.0",
            schemaVersion: 1,
            license: "community",
            publisher: "Test Publisher",
            genre: "sci-fi",
            description: "A test system");
        definition.Tags = ["pool", "sci-fi"];
        definition.IsActive = true;
        definition.IsBuiltIn = false;

        var result = _sut.ApplyDefaults(definition);

        Assert.Equal("my-system", result.Identifier);
        Assert.Equal("My System", result.Name);
        Assert.Equal("2.1.0", result.Version);
        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal("community", result.License);
        Assert.Equal("Test Publisher", result.Publisher);
        Assert.Equal("sci-fi", result.Genre);
        Assert.Equal("A test system", result.Description);
        Assert.Equal(["pool", "sci-fi"], result.Tags);
        Assert.True(result.IsActive);
        Assert.False(result.IsBuiltIn);
    }

    [Fact]
    public void ApplyDefaults_WhenAllOptionalSectionsAreNull_AppliesAllDefaults()
    {
        var definition = CreateMinimalDefinition(
            actionEconomy: null,
            encounterBudget: null,
            aiGuidance: null);

        var result = _sut.ApplyDefaults(definition);

        Assert.NotNull(result.ActionEconomy);
        Assert.Equal(ActionEconomyType.Freeform, result.ActionEconomy.Type);
        Assert.Null(result.EncounterBudget);
        Assert.NotNull(result.AiGuidance);
        Assert.NotNull(result.AiGuidance.SystemPromptNotes);
        Assert.Empty(result.ConditionSet);
    }

    [Fact]
    public void ApplyDefaults_WhenAllOptionalSectionsAreProvided_ChangesNothing()
    {
        var economy = new ActionEconomyDefinition { Type = ActionEconomyType.ActionPoints, PointsPerTurn = 6 };
        var budget = new EncounterBudgetFormula { Type = EncounterBudgetType.XpBudget };
        var guidance = new AiGuidance { SystemPromptNotes = "Custom" };
        var conditions = new List<ConditionDefinition>
        {
            new() { Name = "Stunned" }
        };

        var definition = CreateMinimalDefinition(
            actionEconomy: economy,
            encounterBudget: budget,
            aiGuidance: guidance,
            conditionSet: conditions);

        var result = _sut.ApplyDefaults(definition);

        Assert.Same(economy, result.ActionEconomy);
        Assert.Same(budget, result.EncounterBudget);
        Assert.Same(guidance, result.AiGuidance);
        Assert.Single(result.ConditionSet);
        Assert.Equal("Stunned", result.ConditionSet[0].Name);
    }

    [Fact]
    public void ApplyDefaults_ThrowsArgumentNullException_WhenDefinitionIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.ApplyDefaults(null!));
    }
}
