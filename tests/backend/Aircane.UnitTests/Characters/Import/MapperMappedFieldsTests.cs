using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

/// <summary>
/// Cross-mapper invariants (findings 2/3): a mapper only reports paths it actually populated, never
/// phantom <see cref="CanonicalCharacter"/> defaults, and every <c>RequiresReviewPaths</c> entry is
/// also a <c>MappedFields</c> key whose path the save-time switch accepts.
/// </summary>
public sealed class MapperMappedFieldsTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    public static IEnumerable<object[]> MapperFixtures() => new List<object[]>
    {
        new object[] { new PathbuilderTwoMapper(), "pathbuilder2e.json" },
        new object[] { new DndBeyondApiMapper(), "ddb-v5.json" },
        new object[] { new DndBeyondCompanionMapper(), "ddb-companion.json" },
        new object[] { new FoundryDnd5eMapper(), "foundry-dnd5e.json" },
        new object[] { new FoundryPf2eMapper(), "foundry-pf2e.json" },
        new object[] { new Roll20Mapper(), "roll20.json" },
    };

    [Theory]
    [MemberData(nameof(MapperFixtures))]
    public void MappedFields_OnlyContainSupportedApplyMappingPaths(
        ICharacterSourceMapper mapper, string fixture)
    {
        using var doc = LoadFixture(fixture);
        var result = mapper.Map(doc);

        foreach (var path in result.MappedFields.Keys)
            Assert.Contains(path.ToLowerInvariant(), CanonicalCharacterPaths.Supported);
    }

    [Theory]
    [MemberData(nameof(MapperFixtures))]
    public void RequiresReviewPaths_AreAlsoMappedFieldKeys(
        ICharacterSourceMapper mapper, string fixture)
    {
        using var doc = LoadFixture(fixture);
        var result = mapper.Map(doc);

        foreach (var path in result.RequiresReviewPaths)
            Assert.Contains(path, result.MappedFields.Keys);
    }

    [Theory]
    [MemberData(nameof(MapperFixtures))]
    public void EmptyObject_YieldsNoPhantomDefaultKeys(
        ICharacterSourceMapper mapper, string fixture)
    {
        _ = fixture;
        using var doc = JsonDocument.Parse("{}");
        var result = mapper.Map(doc);

        // None of CanonicalCharacter's non-zero-defaulted paths may appear for an empty input.
        Assert.DoesNotContain(CanonicalCharacterPaths.AbilityStrength, result.MappedFields.Keys);
        Assert.DoesNotContain(CanonicalCharacterPaths.CombatArmorClass, result.MappedFields.Keys);
        Assert.DoesNotContain(CanonicalCharacterPaths.CombatSpeed, result.MappedFields.Keys);
        Assert.DoesNotContain(CanonicalCharacterPaths.CombatMaxHitPoints, result.MappedFields.Keys);
        Assert.DoesNotContain(CanonicalCharacterPaths.CombatHitPoints, result.MappedFields.Keys);
    }

    [Fact]
    public void DndBeyondApi_FixtureMissingSpeed_ProducesNoSpeedKey()
    {
        // ddb-v5.json carries no speed source, so combat.speed must be absent.
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.DoesNotContain(CanonicalCharacterPaths.CombatSpeed, result.MappedFields.Keys);
    }
}
