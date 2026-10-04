using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class GenericVttMapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Order_IsIntMaxValue_SoItRunsLast()
    {
        Assert.Equal(int.MaxValue, new GenericVttMapper().Order);
    }

    [Fact]
    public void CanMap_ReturnsTrue_WhenNameHeuristicMatches()
    {
        var mapper = new GenericVttMapper();
        using var doc = Parse("""{"name":"Someone"}""");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsTrue_WhenAbilityHeuristicMatches()
    {
        var mapper = new GenericVttMapper();
        using var doc = Parse("""{"strength":12}""");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsTrue_ForNestedAbilityContainer()
    {
        var mapper = new GenericVttMapper();
        using var doc = Parse("""{"stats":{"str":14}}""");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenNoNameOrAbilityPresent()
    {
        var mapper = new GenericVttMapper();
        using var doc = Parse("""{"unrelated":true,"foo":"bar"}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new GenericVttMapper();
        using var doc = Parse("[]");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void Map_PullsNameAndNestedAbilities()
    {
        var mapper = new GenericVttMapper();
        using var doc = LoadFixture("generic-vtt.json");

        var result = mapper.Map(doc);
        var c = result.Character;

        Assert.Equal("Test Character", c.Identity.Name);
        Assert.Equal(15, c.Abilities.Strength);
        Assert.Equal(8, c.Abilities.Charisma);
    }

    [Fact]
    public void Map_FlagsEveryMappedPathForReview()
    {
        var mapper = new GenericVttMapper();
        using var doc = LoadFixture("generic-vtt.json");

        var result = mapper.Map(doc);

        Assert.NotEmpty(result.MappedFields);
        foreach (var key in result.MappedFields.Keys)
            Assert.Contains(key, result.RequiresReviewPaths);
    }

    [Fact]
    public void Map_IsLowConfidence_NoForcedSystem_WithWarning()
    {
        var mapper = new GenericVttMapper();
        using var doc = LoadFixture("generic-vtt.json");

        var result = mapper.Map(doc);

        Assert.Equal(ImportConfidence.Low, result.Confidence);
        Assert.Null(result.GameSystemIdentifier);
        Assert.Contains(
            "We couldn't identify this character sheet format. Please review and correct all fields before saving.",
            result.Warnings);
    }

    [Fact]
    public void Map_DoesNotThrow_OnEmptyObject()
    {
        var mapper = new GenericVttMapper();
        using var doc = Parse("{}");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
    }
}
