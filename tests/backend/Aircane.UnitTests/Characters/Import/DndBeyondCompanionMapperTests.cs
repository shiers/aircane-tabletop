using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class DndBeyondCompanionMapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CanMap_ReturnsTrue_ForCompanionExportWithMeta()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = LoadFixture("ddb-companion.json");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsTrue_WhenDdbIdPresentWithoutMeta()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = Parse(
            """{"ddbId":5,"character":{"name":"x","classes":[]}}""");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenNeitherMetaNorDdbId()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = Parse(
            """{"character":{"name":"x","classes":[]}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenCharacterLacksNameOrClasses()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = Parse("""{"_meta":{},"character":{"name":"x"}}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = Parse("5");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void Map_DelegatesToSharedPath_AndMapsFromCharacterEnvelope()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = LoadFixture("ddb-companion.json");

        var result = mapper.Map(doc);

        Assert.Equal("Test Character", result.Character.Identity.Name);
        Assert.Equal("High Elf", result.Character.Identity.RaceOrAncestry);
        Assert.Equal(16, result.Character.Abilities.Dexterity);
        Assert.Equal(3, result.Character.Classes[0].Level);
        Assert.Equal("Wizard", result.Character.Classes[0].ClassName);

        // HP: base 18 + (CON mod +2 * level 3) = 24.
        Assert.Equal(24, result.Character.Combat.MaxHitPoints);
        Assert.Equal(15, result.Character.Combat.ArmorClass);
    }

    [Fact]
    public void Map_SharesRulesetDetection_AndForcesDnd5e2014()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = LoadFixture("ddb-companion.json");

        var result = mapper.Map(doc);

        // sourceId 672 → 2024.
        Assert.Equal("2024", result.Ruleset);
        Assert.True(result.RulesetRequiresConfirmation);
        Assert.Equal("dnd-5e-2014", result.GameSystemIdentifier);
    }

    [Fact]
    public void Map_DoesNotThrow_WhenCharacterEnvelopeMissing()
    {
        var mapper = new DndBeyondCompanionMapper();
        using var doc = Parse("{}");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
    }
}
