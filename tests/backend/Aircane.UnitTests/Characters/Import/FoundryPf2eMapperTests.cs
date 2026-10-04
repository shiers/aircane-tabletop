using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class FoundryPf2eMapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CanMap_ReturnsTrue_ForFoundryPf2eActor()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_ForDnd5eActor()
    {
        // A dnd5e actor has no system.details.ancestry.
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-dnd5e.json");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = Parse("123");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void FoundryDnd5eMapper_DoesNotMatch_APf2eDocument()
    {
        // Negative cross-check: the dnd5e mapper must reject a pf2e actor so the two are mutually
        // exclusive during detection.
        var dnd5e = new FoundryDnd5eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        Assert.False(dnd5e.CanMap(doc));
    }

    [Fact]
    public void Map_PopulatesIdentityClassAndLevel()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        var result = mapper.Map(doc);
        var c = result.Character;

        Assert.Equal("Test Character", c.Identity.Name);
        Assert.Equal("Dwarf", c.Identity.RaceOrAncestry);
        Assert.Equal("Warrior", c.Identity.Background);
        Assert.Single(c.Classes);
        Assert.Equal("Fighter", c.Classes[0].ClassName);
        Assert.Equal(5, c.Classes[0].Level);
        Assert.Equal("5", result.MappedFields[CanonicalCharacterPaths.Level]);
    }

    [Fact]
    public void Map_MapsModifierAbilities()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        var c = mapper.Map(doc).Character;

        // PF2e stores modifiers; the mapper copies them verbatim.
        Assert.Equal(4, c.Abilities.Strength);
        Assert.Equal(2, c.Abilities.Dexterity);
        Assert.Equal(-1, c.Abilities.Charisma);
    }

    [Fact]
    public void Map_PopulatesCombat()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        var c = mapper.Map(doc).Character;

        Assert.Equal(42, c.Combat.CurrentHitPoints);
        Assert.Equal(48, c.Combat.MaxHitPoints);
        Assert.Equal(20, c.Combat.ArmorClass);
        Assert.Equal(25, c.Combat.Speed);
    }

    [Fact]
    public void Map_PutsHeritageSavesAndClassDcInExtraFields()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        var result = mapper.Map(doc);

        Assert.Equal("Ancient-Blooded Dwarf", result.ExtraFields["Heritage"]);
        Assert.Equal("+10", result.ExtraFields["Fortitude"]);
        Assert.Equal("+18", result.ExtraFields["Class DC"]);
    }

    [Fact]
    public void Map_ForcesPathfinderIdentifierAndRuleset()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        var result = mapper.Map(doc);

        Assert.Equal("pathfinder-2e-remaster", result.GameSystemIdentifier);
        Assert.Equal("Remaster", result.Ruleset);
        Assert.Equal(ImportConfidence.High, result.Confidence);
    }

    [Fact]
    public void Map_DoesNotSetSaveOrClassDcPathsOnMappedFields()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = LoadFixture("foundry-pf2e.json");

        var result = mapper.Map(doc);

        foreach (var key in result.MappedFields.Keys)
        {
            Assert.DoesNotContain("fortitude", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reflex", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("will", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("classdc", key.Replace(".", ""), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Map_DoesNotThrow_OnEmptyObject()
    {
        var mapper = new FoundryPf2eMapper();
        using var doc = Parse("{}");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
    }
}
