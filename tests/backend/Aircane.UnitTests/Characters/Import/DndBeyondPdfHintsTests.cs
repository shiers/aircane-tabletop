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

    // ── CaptionMap (OCR path) ─────────────────────────────────────────────────

    [Theory]
    [InlineData("CHARACTER NAME", CanonicalCharacterPaths.IdentityName)]
    [InlineData("SPECIES", CanonicalCharacterPaths.IdentityRaceOrAncestry)]
    [InlineData("RACE", CanonicalCharacterPaths.IdentityRaceOrAncestry)]
    [InlineData("BACKGROUND", CanonicalCharacterPaths.IdentityBackground)]
    [InlineData("STRENGTH", CanonicalCharacterPaths.AbilityStrength)]
    [InlineData("DEXTERITY", CanonicalCharacterPaths.AbilityDexterity)]
    [InlineData("CONSTITUTION", CanonicalCharacterPaths.AbilityConstitution)]
    [InlineData("INTELLIGENCE", CanonicalCharacterPaths.AbilityIntelligence)]
    [InlineData("WISDOM", CanonicalCharacterPaths.AbilityWisdom)]
    [InlineData("CHARISMA", CanonicalCharacterPaths.AbilityCharisma)]
    [InlineData("HIT POINTS", CanonicalCharacterPaths.CombatMaxHitPoints)]
    [InlineData("SPEED", CanonicalCharacterPaths.CombatSpeed)]
    [InlineData("PROFICIENCY BONUS", CanonicalCharacterPaths.CombatProficiencyBonus)]
    public void CaptionMap_MapsCaptionToCanonicalPath(string caption, string expectedPath)
    {
        Assert.True(DndBeyondPdfHints.CaptionMap.TryGetValue(caption, out var path));
        Assert.Equal(expectedPath, path);
    }

    [Theory]
    [InlineData("ARMOR")]
    [InlineData("ARMOR CLASS")]
    public void CaptionMap_ArmorAlias_BothResolveToArmorClass(string caption)
    {
        Assert.True(DndBeyondPdfHints.CaptionMap.TryGetValue(caption, out var path));
        Assert.Equal(CanonicalCharacterPaths.CombatArmorClass, path);
    }

    [Fact]
    public void CaptionMap_IsCaseInsensitive()
    {
        Assert.True(DndBeyondPdfHints.CaptionMap.TryGetValue("strength", out var path));
        Assert.Equal(CanonicalCharacterPaths.AbilityStrength, path);
    }

    [Fact]
    public void CaptionMap_DoesNotContainPassivePerception()
    {
        // PASSIVE PERCEPTION is a signature-quorum token only; ApplyCanonicalPath has no arm for it.
        Assert.False(DndBeyondPdfHints.CaptionMap.ContainsKey(DndBeyondPdfHints.PassivePerceptionCaption));
    }

    [Fact]
    public void CaptionMap_DoesNotContainClassLevel_HandledBySplit()
    {
        Assert.False(DndBeyondPdfHints.CaptionMap.ContainsKey(DndBeyondPdfHints.ClassLevelCaption));
    }

    [Fact]
    public void ClassLevelCaption_IsTheVerifiedToken()
    {
        Assert.Equal("CLASS & LEVEL", DndBeyondPdfHints.ClassLevelCaption);
    }

    [Fact]
    public void CaptionMap_EveryMappedPath_IsSupportedByApplyMapping()
    {
        foreach (var path in DndBeyondPdfHints.CaptionMap.Values)
            Assert.Contains(path.ToLowerInvariant(), CanonicalCharacterPaths.Supported);
    }
}
