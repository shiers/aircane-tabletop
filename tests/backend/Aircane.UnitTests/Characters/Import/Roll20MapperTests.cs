using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class Roll20MapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CanMap_ReturnsTrue_ForRoll20Export()
    {
        var mapper = new Roll20Mapper();
        using var doc = LoadFixture("roll20.json");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WithoutSchemaVersion()
    {
        var mapper = new Roll20Mapper();
        using var doc = Parse("""{"character":{"attribs":[]}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WithoutAttribs()
    {
        var mapper = new Roll20Mapper();
        using var doc = Parse("""{"schema_version":2,"character":{}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new Roll20Mapper();
        using var doc = Parse("null");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void Map_FlatAttribMapping_PopulatesCanonicalCharacter()
    {
        var mapper = new Roll20Mapper();
        using var doc = LoadFixture("roll20.json");

        var c = mapper.Map(doc).Character;

        Assert.Equal("Test Character", c.Identity.Name);
        Assert.Equal("Half-Elf", c.Identity.RaceOrAncestry);
        Assert.Equal("Bard", c.Classes[0].ClassName);
        Assert.Equal(3, c.Classes[0].Level);
        Assert.Equal(16, c.Abilities.Dexterity);
        Assert.Equal(17, c.Abilities.Charisma);
        Assert.Equal(21, c.Combat.CurrentHitPoints);
        Assert.Equal(24, c.Combat.MaxHitPoints);
        Assert.Equal(14, c.Combat.ArmorClass);
    }

    [Fact]
    public void Map_EveryMappedPath_IsAlsoFlaggedForReview()
    {
        var mapper = new Roll20Mapper();
        using var doc = LoadFixture("roll20.json");

        var result = mapper.Map(doc);

        Assert.NotEmpty(result.MappedFields);
        foreach (var key in result.MappedFields.Keys)
            Assert.Contains(key, result.RequiresReviewPaths);
    }

    [Fact]
    public void Map_MissingAttrib_IsOmittedFromMappedFields()
    {
        var mapper = new Roll20Mapper();
        // No "speed" attrib present in the fixture.
        using var doc = LoadFixture("roll20.json");

        var result = mapper.Map(doc);

        Assert.False(result.MappedFields.ContainsKey(CanonicalCharacterPaths.CombatSpeed));
    }

    [Fact]
    public void Map_AlwaysLowConfidence_AndNoForcedSystem()
    {
        var mapper = new Roll20Mapper();
        using var doc = LoadFixture("roll20.json");

        var result = mapper.Map(doc);

        Assert.Equal(ImportConfidence.Low, result.Confidence);
        Assert.Null(result.GameSystemIdentifier);
        Assert.Null(result.Ruleset);
    }

    [Fact]
    public void Map_EmitsNumericValuesAsBareIntegerStrings()
    {
        var mapper = new Roll20Mapper();
        using var doc = LoadFixture("roll20.json");

        var result = mapper.Map(doc);

        Assert.Equal("16", result.MappedFields[CanonicalCharacterPaths.AbilityDexterity]);
        Assert.Equal("24", result.MappedFields[CanonicalCharacterPaths.CombatMaxHitPoints]);
        Assert.Equal("3", result.MappedFields[CanonicalCharacterPaths.Level]);
    }

    [Fact]
    public void Map_DoesNotThrow_OnEmptyAttribs()
    {
        var mapper = new Roll20Mapper();
        using var doc = Parse("""{"schema_version":2,"character":{"attribs":[]}}""");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
        Assert.Empty(result.RequiresReviewPaths);
    }
}
