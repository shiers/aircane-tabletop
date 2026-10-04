using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class CanonicalCharacterPathsTests
{
    [Fact]
    public void SchemaFieldIdToPath_HasNoDuplicateSchemaIds()
    {
        var ids = CanonicalCharacterPaths.SchemaFieldIdToPath.Keys.ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SchemaFieldIdToPath_EveryBridgedPath_IsSupported()
    {
        foreach (var path in CanonicalCharacterPaths.SchemaFieldIdToPath.Values)
            Assert.Contains(path.ToLowerInvariant(), CanonicalCharacterPaths.Supported);
    }

    [Fact]
    public void SchemaFieldIdToPath_BridgeExcludesDerivedPaths()
    {
        // experiencePoints / passivePerception / initiative / proficiencyBonus have no schema field
        // id and are intentionally absent from the bridge table.
        var bridgedPaths = CanonicalCharacterPaths.SchemaFieldIdToPath.Values.ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(CanonicalCharacterPaths.IdentityExperiencePoints, bridgedPaths);
        Assert.DoesNotContain(CanonicalCharacterPaths.PassivePerception, bridgedPaths);
        Assert.DoesNotContain(CanonicalCharacterPaths.CombatInitiative, bridgedPaths);
        Assert.DoesNotContain(CanonicalCharacterPaths.CombatProficiencyBonus, bridgedPaths);
    }

    [Fact]
    public void SchemaFieldIdToPath_RaceAndAncestry_BothBridgeToRaceOrAncestry()
    {
        Assert.Equal(CanonicalCharacterPaths.IdentityRaceOrAncestry,
            CanonicalCharacterPaths.SchemaFieldIdToPath["race"]);
        Assert.Equal(CanonicalCharacterPaths.IdentityRaceOrAncestry,
            CanonicalCharacterPaths.SchemaFieldIdToPath["ancestry"]);
    }
}
