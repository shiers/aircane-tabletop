using Aircane.Application.Abstractions;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

/// <summary>
/// Tests for the DDB OCR → canonical mapper. All data is synthetic (placeholder names, in-range
/// numbers). Verifies the DEFAULT-AND-FLAG contract: unparseable numeric regions stay at the
/// canonical default and are flagged, with NO "defaulting to N" warning.
/// </summary>
public sealed class DndBeyondOcrMapperTests
{
    private static RegionOcrResult Region(string key, string text, float conf = 0.9f) => new(key, text, conf);

    [Fact]
    public void Map_AbilityCaptions_ParseToCanonicalScores()
    {
        var regions = new[]
        {
            Region("STRENGTH", "16 (+3)"),
            Region("DEXTERITY", "14"),
            Region("CONSTITUTION", "12"),
            Region("INTELLIGENCE", "10"),
            Region("WISDOM", "13"),
            Region("CHARISMA", "8"),
        };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);
        var abilities = result.MappedCharacter!.Abilities;

        Assert.Equal(16, abilities.Strength); // modifier suffix stripped
        Assert.Equal(14, abilities.Dexterity);
        Assert.Equal(12, abilities.Constitution);
        Assert.Equal(10, abilities.Intelligence);
        Assert.Equal(13, abilities.Wisdom);
        Assert.Equal(8, abilities.Charisma);
    }

    [Fact]
    public void Map_CombatCaptions_ParseWithUnitStrip()
    {
        var regions = new[]
        {
            Region("ARMOR", "17"),
            Region("HIT POINTS", "25"),
            Region("SPEED", "30 ft."),
            Region("PROFICIENCY BONUS", "3"),
        };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);
        var combat = result.MappedCharacter!.Combat;

        Assert.Equal(17, combat.ArmorClass);
        Assert.Equal(25, combat.MaxHitPoints);
        Assert.Equal(30, combat.Speed); // "30 ft." → 30
        Assert.Equal(3, combat.ProficiencyBonus);
    }

    [Fact]
    public void Map_TextCaptions_ApplyAsIs()
    {
        var regions = new[]
        {
            Region("CHARACTER NAME", "Test Character"),
            Region("SPECIES", "Elf"),
            Region("BACKGROUND", "Sage"),
        };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Dnd2024);
        var identity = result.MappedCharacter!.Identity;

        Assert.Equal("Test Character", identity.Name);
        Assert.Equal("Elf", identity.RaceOrAncestry);
        Assert.Equal("Sage", identity.Background);
    }

    [Fact]
    public void Map_ClassAndLevel_SplitsIntoClassAndLevel()
    {
        var regions = new[] { Region("CLASS & LEVEL", "Fighter 5") };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);
        var classes = result.MappedCharacter!.Classes;

        Assert.Single(classes);
        Assert.Equal("Fighter", classes[0].ClassName);
        Assert.Equal(5, classes[0].Level);
    }

    [Fact]
    public void Map_ClassAndLevel_Multiclass_SplitsEachSegment()
    {
        var regions = new[] { Region("CLASS & LEVEL", "Wizard 3 / Rogue 2") };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);
        var classes = result.MappedCharacter!.Classes;

        Assert.Equal(2, classes.Count);
        Assert.Equal("Wizard", classes[0].ClassName);
        Assert.Equal(3, classes[0].Level);
        Assert.Equal("Rogue", classes[1].ClassName);
        Assert.Equal(2, classes[1].Level);
    }

    [Fact]
    public void Map_EveryProducedField_IsInRequiresReviewPaths()
    {
        var regions = new[]
        {
            Region("STRENGTH", "16"),
            Region("ARMOR", "17"),
            Region("HIT POINTS", "25"),
            Region("SPEED", "30"),
            Region("PROFICIENCY BONUS", "3"),
            Region("CHARACTER NAME", "Test Character"),
            Region("BACKGROUND", "Sage"),
            Region("CLASS & LEVEL", "Fighter 5"),
        };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);
        var review = result.RequiresReviewPaths;

        Assert.Contains(CanonicalCharacterPaths.AbilityStrength, review);
        Assert.Contains(CanonicalCharacterPaths.CombatArmorClass, review);
        Assert.Contains(CanonicalCharacterPaths.CombatMaxHitPoints, review);
        Assert.Contains(CanonicalCharacterPaths.CombatSpeed, review);
        Assert.Contains(CanonicalCharacterPaths.CombatProficiencyBonus, review);
        Assert.Contains(CanonicalCharacterPaths.IdentityName, review);
        Assert.Contains(CanonicalCharacterPaths.IdentityBackground, review);
        Assert.Contains(CanonicalCharacterPaths.Class, review);
        Assert.Contains(CanonicalCharacterPaths.Level, review);
    }

    [Fact]
    public void Map_UnparseableNumericRegion_StaysAtDefault_AndIsFlagged_NoDefaultingWarning()
    {
        // Garbage OCR for a numeric field: must NOT be routed through a canonical apply that would
        // stamp the default and emit a "defaulting to N" warning. Default-and-flag instead.
        var regions = new[]
        {
            Region("STRENGTH", "???"),
            Region("ARMOR", ""),
            Region("HIT POINTS", "garbage"),
            Region("SPEED", "n/a"),
            Region("PROFICIENCY BONUS", "--"),
        };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);
        var character = result.MappedCharacter!;

        // Canonical defaults untouched.
        Assert.Equal(10, character.Abilities.Strength);
        Assert.Equal(10, character.Combat.ArmorClass);
        Assert.Equal(0, character.Combat.MaxHitPoints);
        Assert.Equal(30, character.Combat.Speed);
        Assert.Equal(2, character.Combat.ProficiencyBonus);

        // Still flagged for review (every OCR-mapped field).
        Assert.Contains(CanonicalCharacterPaths.AbilityStrength, result.RequiresReviewPaths);
        Assert.Contains(CanonicalCharacterPaths.CombatArmorClass, result.RequiresReviewPaths);
        Assert.Contains(CanonicalCharacterPaths.CombatMaxHitPoints, result.RequiresReviewPaths);
        Assert.Contains(CanonicalCharacterPaths.CombatSpeed, result.RequiresReviewPaths);
        Assert.Contains(CanonicalCharacterPaths.CombatProficiencyBonus, result.RequiresReviewPaths);

        // No "defaulting to N" warning was emitted.
        Assert.DoesNotContain(result.Warnings, w => w.Contains("defaulting to", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Map_EmptyTextCaption_SkipsButFlags()
    {
        var regions = new[] { Region("CHARACTER NAME", "") };

        var result = DndBeyondOcrMapper.Map(regions, DdbRuleset.Unknown);

        Assert.Equal(string.Empty, result.MappedCharacter!.Identity.Name);
        Assert.Contains(CanonicalCharacterPaths.IdentityName, result.RequiresReviewPaths);
    }

    [Fact]
    public void Map_CarriesRuleset()
    {
        var result = DndBeyondOcrMapper.Map([Region("STRENGTH", "16")], DdbRuleset.Dnd2024);
        Assert.Equal(DdbRuleset.Dnd2024, result.DetectedRuleset);
    }

    [Fact]
    public void Map_ResultIsNotOcrRequired()
    {
        var result = DndBeyondOcrMapper.Map([Region("STRENGTH", "16")], DdbRuleset.Unknown);
        Assert.False(result.IsOcrRequired);
    }

    [Fact]
    public void Map_PassivePerceptionCaption_IsIgnored_NotMapped()
    {
        // PASSIVE PERCEPTION has no CaptionMap entry and no canonical apply arm → ignored, not flagged.
        var result = DndBeyondOcrMapper.Map([Region("PASSIVE PERCEPTION", "14")], DdbRuleset.Unknown);

        Assert.Empty(result.RequiresReviewPaths);
    }
}
