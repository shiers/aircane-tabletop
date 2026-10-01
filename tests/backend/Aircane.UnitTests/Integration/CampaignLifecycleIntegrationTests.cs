using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.Integration;

/// <summary>
/// Integration tests for the full campaign lifecycle using EF Core InMemory provider.
/// Tests the complete flow: create definition → create campaign → create character →
/// roll dice → AI context → condition tracking.
/// </summary>
public class CampaignLifecycleIntegrationTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly SystemRegistry _registry;
    private readonly GameSystemDefinitionSerializer _serializer;
    private readonly MechanicResolver _resolver;
    private readonly CharacterSchemaEngine _schemaEngine;
    private readonly ConditionRegistry _conditionRegistry;
    private readonly AiContextAdapter _aiContextAdapter;

    public CampaignLifecycleIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: $"LifecycleTest_{Guid.NewGuid()}")
            .Options;

        _db = new AircaneDbContext(options);
        _serializer = new GameSystemDefinitionSerializer();
        var validator = new GameSystemDefinitionValidator();
        _registry = new SystemRegistry(_db, _serializer, validator);
        _resolver = new MechanicResolver();
        _schemaEngine = new CharacterSchemaEngine();
        _conditionRegistry = new ConditionRegistry();
        _aiContextAdapter = new AiContextAdapter();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task FullLifecycle_CreateDefinition_CreateCampaign_CreateCharacter_RollDice_AiContext_Conditions()
    {
        // Step 1: Create a custom game system definition
        var definition = CreateCustomDefinition();
        var definitionId = await _registry.CreateAsync(definition);
        Assert.NotEqual(Guid.Empty, definitionId);

        // Step 2: Create a campaign bound to the definition
        var campaign = new Campaign("Test Campaign", "Custom System", "1.0")
        {
            GameSystemDefinitionId = definitionId
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Step 3: Retrieve the definition via the campaign binding
        var loadedDefinition = await _registry.GetByCampaignAsync(campaign.Id);
        Assert.NotNull(loadedDefinition);
        Assert.Equal("custom-test-system", loadedDefinition.Identifier);

        // Step 4: Create a character validated against the schema
        var characterJson = """{"name":"Test Hero","level":5,"str":16,"dex":14,"con":12}""";
        Assert.NotNull(loadedDefinition.CharacterSchema);
        var validationResult = _schemaEngine.Validate(characterJson, loadedDefinition.CharacterSchema);
        Assert.True(validationResult.IsValid, $"Character validation failed: {string.Join(", ", validationResult.Errors)}");

        var character = new Character("Test Hero", "Custom System", "1.0", 5, characterJson, "{}")
        {
            CampaignId = campaign.Id,
            GameSystemDefinitionId = definitionId
        };
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Step 5: Roll dice resolved by campaign's convention
        var convention = loadedDefinition.DiceConventions.First(c => c.Name == "primary");
        var parseResult = _resolver.ParseExpression("1d20+3", convention);
        Assert.True(parseResult.IsSuccess);

        var random = new DefaultRandomSource();
        var rollResult = _resolver.Roll(parseResult.Expression!, convention, random);
        Assert.NotEmpty(rollResult.RawResults);
        Assert.InRange(rollResult.Total, 4, 23); // 1d20 (1-20) + 3

        // Step 6: AI prompt includes system context
        var aiContext = _aiContextAdapter.BuildSystemContext(loadedDefinition);
        Assert.Contains("Custom Test System", aiContext);
        Assert.Contains("Dice Conventions", aiContext);
        Assert.Contains("primary", aiContext);

        // Step 7: Verify condition tracking uses system's condition set
        var conditions = _conditionRegistry.GetConditions(loadedDefinition.ConditionSet);
        Assert.NotEmpty(conditions);
        Assert.True(_conditionRegistry.IsValidCondition(loadedDefinition.ConditionSet, "Poisoned"));
        Assert.False(_conditionRegistry.IsValidCondition(loadedDefinition.ConditionSet, "NonExistentCondition"));

        var poisonedCondition = _conditionRegistry.GetCondition(loadedDefinition.ConditionSet, "Poisoned");
        Assert.NotNull(poisonedCondition);
        Assert.Equal("Poisoned", poisonedCondition.Name);
    }

    [Fact]
    public async Task FullLifecycle_FreeformSystem_AllowsAnyCondition()
    {
        // Create a freeform definition (no conditions defined)
        var definition = new GameSystemDefinition(
            "freeform-test", "Freeform Test", "1.0.0", 1, "user-created")
        {
            DiceConventions = new List<DiceConvention>
            {
                new() { Name = "primary", Type = DiceConventionType.SingleDieModifier, Die = "d20" }
            },
            ResolutionRules = new List<ResolutionRule>
            {
                new() { Name = "check", Type = ResolutionRuleType.TargetNumber, Roll = "primary", Comparison = ">=", TargetSource = "dc" }
            },
            ConditionSet = Array.Empty<ConditionDefinition>()
        };

        var definitionId = await _registry.CreateAsync(definition);
        var loaded = await _registry.GetByIdAsync(definitionId);

        // Freeform mode: any condition name is valid
        Assert.True(_conditionRegistry.IsValidCondition(loaded.ConditionSet, "AnyCustomCondition"));
        Assert.True(_conditionRegistry.IsValidCondition(loaded.ConditionSet, "Bleeding"));
    }

    [Fact]
    public async Task FullLifecycle_DicePoolSystem_CountsSuccesses()
    {
        // Create a dice pool system
        var definition = new GameSystemDefinition(
            "pool-test", "Pool Test", "1.0.0", 1, "user-created")
        {
            DiceConventions = new List<DiceConvention>
            {
                new()
                {
                    Name = "primary",
                    Type = DiceConventionType.DicePoolSuccess,
                    Die = "d6",
                    SuccessThreshold = 5
                }
            },
            ResolutionRules = new List<ResolutionRule>
            {
                new() { Name = "test", Type = ResolutionRuleType.TargetNumber, Roll = "primary", Comparison = ">=", TargetSource = "threshold" }
            }
        };

        var definitionId = await _registry.CreateAsync(definition);

        // Create campaign
        var campaign = new Campaign("Pool Campaign", "Pool System", "1.0")
        {
            GameSystemDefinitionId = definitionId
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Roll dice pool
        var loaded = await _registry.GetByCampaignAsync(campaign.Id);
        var convention = loaded.DiceConventions.First();
        var parseResult = _resolver.ParseExpression("5d6>=5", convention);
        Assert.True(parseResult.IsSuccess);

        var random = new DefaultRandomSource();
        var rollResult = _resolver.Roll(parseResult.Expression!, convention, random);
        Assert.NotNull(rollResult.SuccessCount);
        Assert.InRange(rollResult.SuccessCount.Value, 0, 5);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GameSystemDefinition CreateCustomDefinition()
    {
        return new GameSystemDefinition(
            "custom-test-system", "Custom Test System", "1.0.0", 1, "user-created",
            genre: "fantasy", description: "A custom test system for integration testing.")
        {
            DiceConventions = new List<DiceConvention>
            {
                new()
                {
                    Name = "primary",
                    Type = DiceConventionType.SingleDieModifier,
                    Die = "d20",
                    Description = "Standard d20 roll",
                    ModifierSources = new List<string> { "ability_modifier" }
                }
            },
            ResolutionRules = new List<ResolutionRule>
            {
                new()
                {
                    Name = "check",
                    Type = ResolutionRuleType.TargetNumber,
                    Roll = "primary",
                    Comparison = ">=",
                    TargetSource = "dc"
                }
            },
            CharacterSchema = new CharacterSchema
            {
                Sections = new List<CharacterSchemaSection>
                {
                    new()
                    {
                        Id = "basics",
                        Label = "Basic Information",
                        Fields = new List<CharacterSchemaField>
                        {
                            new() { Id = "name", Type = CharacterFieldType.Text, Label = "Name", Required = true },
                            new() { Id = "level", Type = CharacterFieldType.Number, Label = "Level", Required = true, Min = 1, Max = 20 }
                        }
                    },
                    new()
                    {
                        Id = "abilities",
                        Label = "Abilities",
                        Fields = new List<CharacterSchemaField>
                        {
                            new() { Id = "str", Type = CharacterFieldType.Number, Label = "Strength", Min = 1, Max = 30 },
                            new() { Id = "dex", Type = CharacterFieldType.Number, Label = "Dexterity", Min = 1, Max = 30 },
                            new() { Id = "con", Type = CharacterFieldType.Number, Label = "Constitution", Min = 1, Max = 30 }
                        }
                    }
                }
            },
            ConditionSet = new List<ConditionDefinition>
            {
                new()
                {
                    Name = "Poisoned",
                    Description = "Disadvantage on attack rolls and ability checks.",
                    Effects = new List<ConditionEffect>
                    {
                        new() { Type = "roll_modifier", Scope = "attack_rolls", Effect = "disadvantage" }
                    },
                    DurationType = "until_save",
                    Stackable = false
                },
                new()
                {
                    Name = "Stunned",
                    Description = "Cannot take actions.",
                    Effects = new List<ConditionEffect>
                    {
                        new() { Type = "prevent_action", Scope = "actions" }
                    },
                    DurationType = "rounds",
                    Stackable = false
                }
            },
            ActionEconomy = new ActionEconomyDefinition
            {
                Type = ActionEconomyType.NamedSlots,
                TurnStructure = new TurnStructure
                {
                    Slots = new List<ActionSlot>
                    {
                        new() { Name = "action", Label = "Action", Count = 1 },
                        new() { Name = "bonus_action", Label = "Bonus Action", Count = 1 }
                    }
                }
            },
            AiGuidance = new AiGuidance
            {
                SystemPromptNotes = "This is a d20-based system.",
                ToneGuidance = "Heroic fantasy",
                MechanicalNotes = "Roll d20+modifier vs DC."
            }
        };
    }
}
