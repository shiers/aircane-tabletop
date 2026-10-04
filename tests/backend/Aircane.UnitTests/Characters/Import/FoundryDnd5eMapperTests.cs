using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class FoundryDnd5eMapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CanMap_ReturnsTrue_ForFoundryDnd5eActor()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_ForPf2eActor()
    {
        // A pf2e actor carries system.details.ancestry and must be rejected by the dnd5e mapper.
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenNotCharacterType()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse("""{"type":"npc","system":{"abilities":{},"attributes":{"hp":{}}}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse("[]");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void Map_PopulatesIdentityAbilitiesAndCombat()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var c = mapper.Map(doc).Character;

        Assert.Equal("Test Character", c.Identity.Name);
        Assert.Equal("Dwarf", c.Identity.RaceOrAncestry);
        Assert.Equal("Soldier", c.Identity.Background);

        Assert.Equal(16, c.Abilities.Strength);
        Assert.Equal(8, c.Abilities.Charisma);

        Assert.Equal(28, c.Combat.CurrentHitPoints);
        Assert.Equal(34, c.Combat.MaxHitPoints);
        Assert.Equal(17, c.Combat.ArmorClass);
        Assert.Equal(30, c.Combat.Speed);
    }

    [Fact]
    public void Map_ReadsClassAndSubclassFromItems()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var c = mapper.Map(doc).Character;

        Assert.Single(c.Classes);
        Assert.Equal("Fighter", c.Classes[0].ClassName);
        Assert.Equal("Champion", c.Classes[0].Subclass);
    }

    [Fact]
    public void Map_UsesDetailsLevel_WhenPresent()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var result = mapper.Map(doc);

        Assert.Equal(4, result.Character.Classes[0].Level);
        Assert.Equal("4", result.MappedFields[CanonicalCharacterPaths.Level]);
    }

    [Fact]
    public void Map_SumsClassItemLevels_WhenDetailsLevelAbsent()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse(
            """
            {
              "name":"Test Character","type":"character",
              "system":{"abilities":{},"attributes":{"hp":{}},"details":{}},
              "items":[
                {"type":"class","name":"Fighter","system":{"levels":3}},
                {"type":"class","name":"Rogue","system":{"levels":2}}
              ]
            }
            """);

        var result = mapper.Map(doc);

        Assert.Equal(5, result.Character.Classes[0].Level);
        Assert.Equal("5", result.MappedFields[CanonicalCharacterPaths.Level]);
    }

    [Fact]
    public void Map_SystemVersionV3_YieldsRuleset2024_NoConfirmation()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var result = mapper.Map(doc);

        Assert.Equal("2024", result.Ruleset);
        Assert.False(result.RulesetRequiresConfirmation);
    }

    [Fact]
    public void Map_SystemVersionV2_YieldsRuleset2014_NoConfirmation()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse(
            """
            {"name":"T","type":"character","_stats":{"systemVersion":"2.4.1"},
             "system":{"abilities":{},"attributes":{"hp":{}}}}
            """);

        var result = mapper.Map(doc);

        Assert.Equal("2014", result.Ruleset);
        Assert.False(result.RulesetRequiresConfirmation);
    }

    [Fact]
    public void Map_SystemVersionAbsent_Defaults2014_AndRequiresConfirmation()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse(
            """{"name":"T","type":"character","system":{"abilities":{},"attributes":{"hp":{}}}}""");

        var result = mapper.Map(doc);

        Assert.Equal("2014", result.Ruleset);
        Assert.True(result.RulesetRequiresConfirmation);
    }

    [Fact]
    public void Map_ForcesDnd5e2014Identifier_AndHighConfidence()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var result = mapper.Map(doc);

        Assert.Equal("dnd-5e-2014", result.GameSystemIdentifier);
        Assert.Equal(ImportConfidence.High, result.Confidence);
    }

    [Fact]
    public void Map_OmitsPhantomDefaultKeys_WhenCombatAbsent()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse(
            """{"name":"T","type":"character","system":{"abilities":{},"attributes":{"hp":{}}}}""");

        var result = mapper.Map(doc);

        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.CombatArmorClass));
        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.CombatSpeed));
        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.AbilityStrength));
    }

    [Fact]
    public void Map_SubtypeGoesToExtraFields_AsSubrace()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var result = mapper.Map(doc);

        Assert.Equal("Mountain Dwarf", result.ExtraFields["Subrace"]);
    }

    [Fact]
    public void Map_EmitsNumericValuesAsBareIntegerStrings()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        var result = mapper.Map(doc);

        Assert.Equal("16", result.MappedFields[CanonicalCharacterPaths.AbilityStrength]);
        Assert.Equal("34", result.MappedFields[CanonicalCharacterPaths.CombatMaxHitPoints]);
        Assert.Equal("17", result.MappedFields[CanonicalCharacterPaths.CombatArmorClass]);
    }

    [Fact]
    public void Map_DoesNotThrow_OnEmptyObject()
    {
        var mapper = new FoundryDnd5eMapper();
        using var doc = Parse("{}");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
    }
}
