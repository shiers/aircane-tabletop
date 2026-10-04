using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

/// <summary>
/// Tests for the DDB printable-sheet signature detector. All caption tokens are template text
/// (not PII) from the verified tuning note.
/// </summary>
public sealed class DndBeyondSheetSignatureTests
{
    private static readonly string[] AllAbilities =
    [
        "STRENGTH", "DEXTERITY", "CONSTITUTION", "INTELLIGENCE", "WISDOM", "CHARISMA",
    ];

    [Fact]
    public void Detect_QuorumMet_WithAbilitiesAndTwoCorroborating_IsMatch()
    {
        var captions = AllAbilities
            .Concat(["CHARACTER NAME", "ARMOR"]) // two corroborating
            .ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Detect_AllSixAbilities_WithOnlyOneCorroborating_IsNotMatch()
    {
        var captions = AllAbilities.Concat(["ARMOR"]).ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Detect_MissingOneAbility_IsNotMatch()
    {
        var captions = AllAbilities.Take(5)
            .Concat(["CHARACTER NAME", "ARMOR", "PASSIVE PERCEPTION"])
            .ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Detect_ArbitraryCaptions_IsNotMatch()
    {
        var captions = new[] { "TITLE", "AUTHOR", "DATE", "SUMMARY" };

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Detect_IsCaseInsensitive_AndTrims()
    {
        var captions = new[]
        {
            " strength ", "Dexterity", "constitution", "INTELLIGENCE", "wisdom", "Charisma",
            "class & level", "passive perception",
        };

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Detect_Species_SelectsDnd2024()
    {
        var captions = AllAbilities.Concat(["CHARACTER NAME", "ARMOR", "SPECIES"]).ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.Equal(DdbRuleset.Dnd2024, result.Ruleset);
    }

    [Fact]
    public void Detect_Race_SelectsDnd2014()
    {
        var captions = AllAbilities.Concat(["CHARACTER NAME", "ARMOR", "RACE"]).ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.Equal(DdbRuleset.Dnd2014, result.Ruleset);
    }

    [Fact]
    public void Detect_SpeciesAndRaceBothPresent_SpeciesWins_2024()
    {
        var captions = AllAbilities.Concat(["CHARACTER NAME", "ARMOR", "SPECIES", "RACE"]).ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.Equal(DdbRuleset.Dnd2024, result.Ruleset);
    }

    [Fact]
    public void Detect_NeitherSpeciesNorRace_RulesetUnknown()
    {
        var captions = AllAbilities.Concat(["CHARACTER NAME", "ARMOR"]).ToList();

        var result = DndBeyondSheetSignature.Detect(captions);

        Assert.Equal(DdbRuleset.Unknown, result.Ruleset);
    }

    [Fact]
    public void Detect_EmptyCaptions_IsNotMatch_UnknownRuleset()
    {
        var result = DndBeyondSheetSignature.Detect([]);

        Assert.False(result.IsMatch);
        Assert.Equal(DdbRuleset.Unknown, result.Ruleset);
    }
}
