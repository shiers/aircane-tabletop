using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class PathbuilderTwoMapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CanMap_ReturnsTrue_ForPathbuilderExport()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenRootHasSystem()
    {
        // A Foundry actor carries a `system` object; Pathbuilder detection must exclude it even
        // when build/class/ancestry are present.
        var mapper = new PathbuilderTwoMapper();
        using var doc = Parse(
            """{"system":{},"build":{"class":"Fighter","ancestry":"Dwarf"}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenBuildMissingClassOrAncestry()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = Parse("""{"build":{"class":"Fighter"}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = Parse("[]");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void Map_PopulatesIdentityClassAndAbilities()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);
        var c = result.Character;

        Assert.Equal("Test Character", c.Identity.Name);
        Assert.Equal("Dwarf", c.Identity.RaceOrAncestry);
        Assert.Equal("Warrior", c.Identity.Background);
        Assert.Single(c.Classes);
        Assert.Equal("Fighter", c.Classes[0].ClassName);
        Assert.Equal(5, c.Classes[0].Level);

        Assert.Equal(18, c.Abilities.Strength);
        Assert.Equal(14, c.Abilities.Dexterity);
        Assert.Equal(16, c.Abilities.Constitution);
        Assert.Equal(10, c.Abilities.Intelligence);
        Assert.Equal(12, c.Abilities.Wisdom);
        Assert.Equal(8, c.Abilities.Charisma);
    }

    [Fact]
    public void Map_PopulatesHpAcSpeed_AndDerivesCurrentHp()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);
        var c = result.Character;

        Assert.Equal(68, c.Combat.MaxHitPoints);
        Assert.Equal(68, c.Combat.CurrentHitPoints);
        Assert.Equal(22, c.Combat.ArmorClass);
        Assert.Equal(20, c.Combat.Speed);
    }

    [Fact]
    public void Map_EmitsNumericValuesAsBareIntegerStrings()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);

        Assert.Equal("18", result.MappedFields[CanonicalCharacterPaths.AbilityStrength]);
        Assert.Equal("68", result.MappedFields[CanonicalCharacterPaths.CombatMaxHitPoints]);
        Assert.Equal("22", result.MappedFields[CanonicalCharacterPaths.CombatArmorClass]);
        Assert.Equal("5", result.MappedFields[CanonicalCharacterPaths.Level]);
    }

    [Fact]
    public void Map_ForcesPathfinderIdentifierAndRuleset()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);

        Assert.Equal("pathfinder-2e-remaster", result.GameSystemIdentifier);
        Assert.Equal("Remaster", result.Ruleset);
        Assert.Equal(ImportConfidence.High, result.Confidence);
    }

    [Fact]
    public void Map_PutsSavesAndLoresInExtraFields_WithRankToBonusConversion()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);

        // Trained (rank 1) at level 5 => 1*2 + 5 = +7
        Assert.Equal("+7", result.ExtraFields["Fortitude"]);
        Assert.Equal("+7", result.ExtraFields["Warfare Lore"]);
        Assert.True(result.ExtraFields.ContainsKey("Heritage"));
        Assert.True(result.ExtraFields.ContainsKey("Feats"));
    }

    [Fact]
    public void Map_DoesNotSetAnyPf2eSaveOrClassDcPathOnMappedFields()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);

        // Saves, class DC, perception, heritage, etc. have no ApplyMapping path and must never
        // appear as mapped canonical fields.
        foreach (var key in result.MappedFields.Keys)
        {
            Assert.DoesNotContain("fortitude", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reflex", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("will", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("classdc", key.Replace(".", ""), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("perception", key, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Map_OmitsPhantomDefaultKeys_WhenFieldsAbsent()
    {
        var mapper = new PathbuilderTwoMapper();
        // Minimal build: class + ancestry only (so CanMap passes), no abilities/attributes.
        using var doc = Parse("""{"build":{"class":"Rogue","ancestry":"Elf"}}""");

        var result = mapper.Map(doc);

        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.AbilityStrength));
        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.CombatArmorClass));
        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.CombatSpeed));
        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.CombatMaxHitPoints));
        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.Level));
        // The field that WAS present maps.
        Assert.Equal("Rogue", result.MappedFields[CanonicalCharacterPaths.Class]);
    }

    [Fact]
    public void Map_DerivedCurrentHp_IsInRequiresReview_AndEveryRequiresReviewEntryIsMapped()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = LoadFixture("pathbuilder2e.json");

        var result = mapper.Map(doc);

        Assert.Contains(CanonicalCharacterPaths.CombatHitPoints, result.RequiresReviewPaths);
        Assert.All(result.RequiresReviewPaths, p => Assert.True(result.MappedFields.ContainsKey(p)));
    }

    [Fact]
    public void Map_DoesNotThrow_OnEmptyObject()
    {
        var mapper = new PathbuilderTwoMapper();
        using var doc = Parse("{}");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
    }
}
