using Aircane.Application.GameSystems;
using Aircane.Domain.Combat;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for <see cref="AiContextAdapter.BuildCombatContext"/> — the live combat summary
/// injected into the AI DM prompt.
/// </summary>
public class AiContextAdapterCombatTests
{
    private readonly AiContextAdapter _sut = new();

    private static EncounterState ActiveEncounter()
    {
        var hero = new Combatant
        {
            Id = "hero", Name = "Hero", IsPlayerCharacter = true,
            CurrentHp = 18, MaxHp = 24, Initiative = 17,
        };
        var goblin = new Combatant
        {
            Id = "goblin", Name = "Goblin", IsPlayerCharacter = false,
            CurrentHp = 4, MaxHp = 12, Initiative = 9,
            Conditions = [new ConditionInstance { Name = "Prone", RemainingRounds = 2 }],
        };

        return new EncounterState
        {
            IsActive = true,
            Round = 3,
            TurnIndex = 0,
            InitiativeOrder = ["hero", "goblin"],
            Combatants = [hero, goblin],
        };
    }

    private static GameSystemDefinition Dnd5eLikeDefinition() => new(
        identifier: "dnd-5e-2014", name: "D&D 5e", version: "1.0.0", schemaVersion: 1, license: "built-in")
    {
        ActionEconomy = new ActionEconomyDefinition
        {
            Type = ActionEconomyType.NamedSlots,
            TurnStructure = new TurnStructure
            {
                Slots =
                [
                    new ActionSlot { Name = "action", Label = "Action", Count = 1 },
                    new ActionSlot { Name = "bonus", Label = "Bonus Action", Count = 1 },
                    new ActionSlot { Name = "reaction", Label = "Reaction", Count = 1 },
                ],
            },
        },
    };

    private static GameSystemDefinition FreeformDefinition() => new(
        identifier: "freeform", name: "Freeform", version: "1.0.0", schemaVersion: 1, license: "built-in")
    {
        ActionEconomy = new ActionEconomyDefinition { Type = ActionEconomyType.Freeform },
    };

    [Fact]
    public void BuildCombatContext_NullOrInactive_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _sut.BuildCombatContext(null, Dnd5eLikeDefinition()));

        var inactive = ActiveEncounter() with { IsActive = false };
        Assert.Equal(string.Empty, _sut.BuildCombatContext(inactive, Dnd5eLikeDefinition()));
    }

    [Fact]
    public void BuildCombatContext_IncludesRoundActiveTurnAndHp()
    {
        var text = _sut.BuildCombatContext(ActiveEncounter(), Dnd5eLikeDefinition());

        Assert.Contains("Round: 3", text);
        Assert.Contains("Active turn: Hero", text);
        Assert.Contains("Hero", text);
        Assert.Contains("18/24", text);
        Assert.Contains("Goblin", text);
        Assert.Contains("4/12", text);
    }

    [Fact]
    public void BuildCombatContext_IncludesConditions()
    {
        var text = _sut.BuildCombatContext(ActiveEncounter(), Dnd5eLikeDefinition());
        Assert.Contains("Prone", text);
        Assert.Contains("2r", text);
    }

    [Fact]
    public void BuildCombatContext_StructuredSystem_IncludesActionSlots()
    {
        var text = _sut.BuildCombatContext(ActiveEncounter(), Dnd5eLikeDefinition());
        Assert.Contains("Action", text);
        Assert.Contains("Bonus Action", text);
        Assert.Contains("Reaction", text);
    }

    [Fact]
    public void BuildCombatContext_FreeformSystem_OmitsActionSlots()
    {
        var text = _sut.BuildCombatContext(ActiveEncounter(), FreeformDefinition());
        Assert.DoesNotContain("action slots", text);
    }

    [Fact]
    public void BuildCombatContext_NullDefinition_StillRendersRosterWithoutSlots()
    {
        var text = _sut.BuildCombatContext(ActiveEncounter(), null);
        Assert.Contains("Round: 3", text);
        Assert.Contains("Hero", text);
        Assert.DoesNotContain("action slots", text);
    }

    [Fact]
    public void BuildCombatContext_LargeRoster_TruncatesAroundActive()
    {
        var combatants = new List<Combatant>();
        var order = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var id = $"c{i}";
            combatants.Add(new Combatant { Id = id, Name = $"Fighter{i}", CurrentHp = 10, MaxHp = 10 });
            order.Add(id);
        }

        var enc = new EncounterState
        {
            IsActive = true,
            Round = 1,
            TurnIndex = 0,
            InitiativeOrder = order,
            Combatants = combatants,
        };

        var text = _sut.BuildCombatContext(enc, null);
        Assert.Contains("not shown", text);
    }
}
