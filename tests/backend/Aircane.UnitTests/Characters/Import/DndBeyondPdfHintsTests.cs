using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class DndBeyondPdfHintsTests
{
    [Theory]
    [InlineData("CharacterName", CanonicalCharacterPaths.IdentityName)]
    [InlineData("Race", CanonicalCharacterPaths.IdentityRaceOrAncestry)]
    [InlineData("Background", CanonicalCharacterPaths.IdentityBackground)]
    [InlineData("STR", CanonicalCharacterPaths.AbilityStrength)]
    [InlineData("DEX", CanonicalCharacterPaths.AbilityDexterity)]
    [InlineData("CON", CanonicalCharacterPaths.AbilityConstitution)]
    [InlineData("INT", CanonicalCharacterPaths.AbilityIntelligence)]
    [InlineData("WIS", CanonicalCharacterPaths.AbilityWisdom)]
    [InlineData("CHA", CanonicalCharacterPaths.AbilityCharisma)]
    [InlineData("HPMax", CanonicalCharacterPaths.CombatMaxHitPoints)]
    [InlineData("HPCurrent", CanonicalCharacterPaths.CombatHitPoints)]
    [InlineData("AC", CanonicalCharacterPaths.CombatArmorClass)]
    [InlineData("Speed", CanonicalCharacterPaths.CombatSpeed)]
    [InlineData("ProfBonus", CanonicalCharacterPaths.CombatProficiencyBonus)]
    public void FieldMap_MapsDndBeyondFieldToCanonicalPath(string field, string expectedPath)
    {
        Assert.True(DndBeyondPdfHints.FieldMap.TryGetValue(field, out var path));
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void FieldMap_IsCaseInsensitive()
    {
        Assert.True(DndBeyondPdfHints.FieldMap.TryGetValue("charactername", out var path));
        Assert.Equal(CanonicalCharacterPaths.IdentityName, path);
    }

    [Fact]
    public void FieldMap_DoesNotMapClassLevel_HandledBySplitLogic()
    {
        // ClassLevel is handled by a dedicated split step, not the direct field map.
        Assert.False(DndBeyondPdfHints.FieldMap.ContainsKey(DndBeyondPdfHints.ClassLevelFieldName));
    }

    [Fact]
    public void FieldMap_EveryMappedPath_IsSupportedByApplyMapping()
    {
        foreach (var path in DndBeyondPdfHints.FieldMap.Values)
            Assert.Contains(path.ToLowerInvariant(), CanonicalCharacterPaths.Supported);
    }
}
