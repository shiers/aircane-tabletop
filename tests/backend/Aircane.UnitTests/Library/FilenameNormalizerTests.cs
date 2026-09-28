using Aircane.Application.Library;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for <see cref="FilenameNormalizer"/>. Mirrors the frontend filenameSuggestions tests
/// so the backend (source of truth for the folder-scan flow) stays behaviorally aligned.
/// </summary>
public class FilenameNormalizerTests
{
    [Theory]
    [InlineData("Player Handbook.pdf", "Player Handbook")]
    [InlineData("Dungeon Masters Guide (Color OCR).pdf", "Dungeon Masters Guide")]
    [InlineData("Players Handbook (BnW OCR).pdf", "Players Handbook")]
    [InlineData("Mordenkainen's Tome of Foes [Deluxe].pdf", "Mordenkainen's Tome of Foes")]
    [InlineData("DnD 5e Dungeon Masters Guide (Color OCR).pdf", "D&D 5e Dungeon Masters Guide")]
    [InlineData("D&D5e Players Handbook.pdf", "D&D 5e Players Handbook")]
    [InlineData("DnD Adventures.pdf", "D&D Adventures")]
    [InlineData("Pf2e Core Rules.pdf", "Pathfinder 2e Core Rules")]
    [InlineData("Volos_Guide__to_Monsters.pdf", "Volos Guide to Monsters")]
    [InlineData("Some Book - (Color OCR).pdf", "Some Book")]
    [InlineData("Rules v1.2 Errata.pdf", "Rules v1.2 Errata")]
    // Variant qualifiers are dropped for dedup but PRESERVED in the suggested title.
    [InlineData("Mordenkainen's Tome of Foes Deluxe.pdf", "Mordenkainen's Tome of Foes Deluxe")]
    public void SuggestTitle_ReturnsCleanedTitle(string fileName, string expected)
    {
        Assert.Equal(expected, FilenameNormalizer.SuggestTitle(fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SuggestTitle_EmptyInput_ReturnsEmpty(string? fileName)
    {
        Assert.Equal(string.Empty, FilenameNormalizer.SuggestTitle(fileName));
    }

    [Theory]
    [InlineData("D&D 5e Players Handbook 2014.pdf", "2014")]
    [InlineData("Players Handbook (2024).pdf", "2024")]
    public void DetectRulesetYear_FindsStandaloneYear(string fileName, string expected)
    {
        Assert.Equal(expected, FilenameNormalizer.DetectRulesetYear(fileName));
    }

    [Theory]
    [InlineData("Dungeon Masters Guide (Color OCR).pdf")]
    [InlineData("Book12014edition.pdf")]
    [InlineData("Roll 1800 gold.pdf")] // 18xx out of 19xx/20xx range
    [InlineData("Page 3021.pdf")] // 30xx out of range
    [InlineData("")]
    public void DetectRulesetYear_NoPlausibleYear_ReturnsNull(string fileName)
    {
        Assert.Null(FilenameNormalizer.DetectRulesetYear(fileName));
    }

    [Fact]
    public void ComputeDedupKey_VariantsOfSameWork_ProduceSameKey()
    {
        var bnw = FilenameNormalizer.ComputeDedupKey("DnD 5e Players Handbook (BnW OCR).pdf");
        var color = FilenameNormalizer.ComputeDedupKey("DnD 5e Players Handbook (Color OCR).pdf");

        Assert.Equal(bnw, color);
        Assert.NotEqual(string.Empty, bnw);
    }

    [Theory]
    [InlineData("Mordenkainen's Tome of Foes Deluxe.pdf")]
    [InlineData("Mordenkainen's Tome of Foes (Deluxe).pdf")]
    [InlineData("Mordenkainen's Tome of Foes - Revised.pdf")]
    [InlineData("Mordenkainen's Tome of Foes Special Edition.pdf")]
    [InlineData("Mordenkainen's Tome of Foes OCR.pdf")]
    public void ComputeDedupKey_BareOrBracketedVariantQualifiers_MatchPlain(string variant)
    {
        var variantKey = FilenameNormalizer.ComputeDedupKey(variant);
        var plain = FilenameNormalizer.ComputeDedupKey("Mordenkainen's Tome of Foes.pdf");

        Assert.Equal(plain, variantKey);
    }

    [Fact]
    public void ComputeDedupKey_DiffersByYearOnly_ProduceSameKey()
    {
        var y2014 = FilenameNormalizer.ComputeDedupKey("Players Handbook 2014.pdf");
        var noYear = FilenameNormalizer.ComputeDedupKey("Players Handbook.pdf");

        Assert.Equal(y2014, noYear);
    }

    [Fact]
    public void ComputeDedupKey_DifferentWorks_ProduceDifferentKeys()
    {
        var phb = FilenameNormalizer.ComputeDedupKey("Players Handbook.pdf");
        var dmg = FilenameNormalizer.ComputeDedupKey("Dungeon Masters Guide.pdf");

        Assert.NotEqual(phb, dmg);
    }
}
