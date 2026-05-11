using Aircane.Application.Characters;
using Aircane.Infrastructure.Characters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Characters;

/// <summary>
/// Unit tests for <see cref="PdfCharacterExtractor"/>.
/// Tests use in-memory PDF byte streams built with PdfPig's document builder
/// or minimal raw byte arrays to exercise the extractor's behaviour.
/// </summary>
public class PdfCharacterExtractorTests
{
    private readonly PdfCharacterExtractor _extractor =
        new(NullLogger<PdfCharacterExtractor>.Instance);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a stream containing a minimal valid PDF with no text and no form fields.
    /// PdfPig can open it but will find nothing to extract, triggering IsOcrRequired.
    /// </summary>
    private static Stream EmptyPdfStream()
    {
        // Minimal valid PDF 1.4 with one empty page — no text, no AcroForm
        const string minimalPdf = "%PDF-1.4\n" +
                                  "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                                  "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
                                  "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n" +
                                  "xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n" +
                                  "trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n190\n%%EOF";

        return new MemoryStream(System.Text.Encoding.ASCII.GetBytes(minimalPdf));
    }

    /// <summary>
    /// Returns a stream that is not a valid PDF at all (random bytes).
    /// </summary>
    private static Stream InvalidPdfStream()
        => new MemoryStream([0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]);

    // ── Empty / unreadable PDF ────────────────────────────────────────────────

    [Fact]
    public async Task EmptyPdf_ReturnsIsOcrRequired_True()
    {
        using var stream = EmptyPdfStream();

        var result = await _extractor.ExtractAsync(stream);

        Assert.True(result.IsOcrRequired);
        Assert.Empty(result.ExtractedFields);
        Assert.Null(result.MappedCharacter);
    }

    [Fact]
    public async Task InvalidPdfBytes_ReturnsIsOcrRequired_True()
    {
        using var stream = InvalidPdfStream();

        var result = await _extractor.ExtractAsync(stream);

        Assert.True(result.IsOcrRequired);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task EmptyPdf_HasAtLeastOneWarning()
    {
        using var stream = EmptyPdfStream();

        var result = await _extractor.ExtractAsync(stream);

        // Either the "no content" warning or a parse warning should be present
        Assert.NotEmpty(result.Warnings);
    }

    // ── Field mapping ─────────────────────────────────────────────────────────

    [Fact]
    public void MapToCanonical_CharacterName_IsMapped()
    {
        // Exercise the mapping logic directly via a text-layer PDF simulation
        // by calling the internal mapping through a crafted text extraction.
        // We test the mapper indirectly through the public ExtractAsync API
        // using a PDF with text content.

        // Build a PDF with "CharacterName: Aldric" text
        var result = ExtractFromText("CharacterName: Aldric\nSTR: 16\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal("Aldric", result.MappedCharacter!.Identity.Name);
    }

    [Fact]
    public void MapToCanonical_AbilityScores_AreMapped()
    {
        var result = ExtractFromText(
            "CharacterName: Thorn\nSTR: 18\nDEX: 14\nCON: 16\nINT: 10\nWIS: 12\nCHA: 8\n");

        Assert.NotNull(result.MappedCharacter);
        var abilities = result.MappedCharacter!.Abilities;
        Assert.Equal(18, abilities.Strength);
        Assert.Equal(14, abilities.Dexterity);
        Assert.Equal(16, abilities.Constitution);
        Assert.Equal(10, abilities.Intelligence);
        Assert.Equal(12, abilities.Wisdom);
        Assert.Equal(8, abilities.Charisma);
    }

    [Fact]
    public void MapToCanonical_NonNumericAbilityScore_AddsWarning()
    {
        var result = ExtractFromText("CharacterName: Thorn\nSTR: abc\n");

        Assert.NotNull(result.MappedCharacter);
        // Default value applied
        Assert.Equal(10, result.MappedCharacter!.Abilities.Strength);
        // Warning recorded
        Assert.Contains(result.Warnings, w => w.Contains("STR") && w.Contains("abc"));
    }

    [Fact]
    public void MapToCanonical_UnknownFields_AreCollectedAsUnmapped()
    {
        var result = ExtractFromText(
            "CharacterName: Thorn\nFavoriteColor: Blue\nPetName: Fluffy\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Contains("FavoriteColor", result.UnmappedFields.Keys);
        Assert.Contains("PetName", result.UnmappedFields.Keys);
        Assert.Equal("Blue", result.UnmappedFields["FavoriteColor"]);
    }

    [Fact]
    public void MapToCanonical_CombatFields_AreMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nAC: 15\nHPMax: 42\nSpeed: 35\n");

        Assert.NotNull(result.MappedCharacter);
        var combat = result.MappedCharacter!.Combat;
        Assert.Equal(15, combat.ArmorClass);
        Assert.Equal(42, combat.MaxHitPoints);
        Assert.Equal(35, combat.Speed);
    }

    [Fact]
    public void MapToCanonical_ClassLevel_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nClassLevel: Fighter 5\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Single(result.MappedCharacter!.Classes);
        Assert.Equal("Fighter", result.MappedCharacter.Classes[0].ClassName);
        Assert.Equal(5, result.MappedCharacter.Classes[0].Level);
    }

    [Fact]
    public void MapToCanonical_Race_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nRace: Half-Elf\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal("Half-Elf", result.MappedCharacter!.Identity.RaceOrAncestry);
    }

    [Fact]
    public void MapToCanonical_Background_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nBackground: Soldier\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal("Soldier", result.MappedCharacter!.Identity.Background);
    }

    [Fact]
    public void MapToCanonical_SpeedWithFtSuffix_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nSpeed: 30 ft.\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(30, result.MappedCharacter!.Combat.Speed);
    }

    [Fact]
    public void MapToCanonical_AbilityScoreWithModifier_IsMapped()
    {
        // Some PDFs include the modifier in parentheses: "16 (+3)"
        var result = ExtractFromText("CharacterName: Thorn\nSTR: 16 (+3)\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(16, result.MappedCharacter!.Abilities.Strength);
    }

    [Fact]
    public void MapToCanonical_OnlyUnknownFields_MappedCharacterHasEmptyName()
    {
        var result = ExtractFromText("FavoriteColor: Blue\nPetName: Fluffy\n");

        // Character is mapped but name is empty (no CharacterName field)
        Assert.NotNull(result.MappedCharacter);
        Assert.Empty(result.MappedCharacter!.Identity.Name);
        Assert.Equal(2, result.UnmappedFields.Count);
    }

    [Fact]
    public void MapToCanonical_ProficiencyBonus_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nProficiencyBonus: 3\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(3, result.MappedCharacter!.Combat.ProficiencyBonus);
    }

    [Fact]
    public void MapToCanonical_SavingThrows_AreMapped()
    {
        var result = ExtractFromText(
            "CharacterName: Thorn\nStrSave: Yes\nDexSave: Yes\nConSave: No\nIntSave: No\nWisSave: Yes\nChaSave: No\n");

        Assert.NotNull(result.MappedCharacter);
        var saves = result.MappedCharacter!.SavingThrows;
        Assert.True(saves.Strength);
        Assert.True(saves.Dexterity);
        Assert.False(saves.Constitution);
        Assert.False(saves.Intelligence);
        Assert.True(saves.Wisdom);
        Assert.False(saves.Charisma);
    }

    [Fact]
    public void MapToCanonical_SavingThrow_TrueValues_Recognized()
    {
        // "1", "true", "x", "on", "checked" should all map to true
        var result = ExtractFromText(
            "CharacterName: Thorn\nStrSave: 1\nDexSave: true\nConSave: x\nIntSave: on\nWisSave: checked\nChaSave: 0\n");

        Assert.NotNull(result.MappedCharacter);
        var saves = result.MappedCharacter!.SavingThrows;
        Assert.True(saves.Strength);
        Assert.True(saves.Dexterity);
        Assert.True(saves.Constitution);
        Assert.True(saves.Intelligence);
        Assert.True(saves.Wisdom);
        Assert.False(saves.Charisma);
    }

    [Fact]
    public void MapToCanonical_Skills_Proficient_AreMapped()
    {
        var result = ExtractFromText(
            "CharacterName: Thorn\nAthletics: Yes\nStealth: Yes\nPerception: Yes\n");

        Assert.NotNull(result.MappedCharacter);
        var skills = result.MappedCharacter!.Skills;

        var athletics = skills.FirstOrDefault(s => s.SkillName == "Athletics");
        var stealth = skills.FirstOrDefault(s => s.SkillName == "Stealth");
        var perception = skills.FirstOrDefault(s => s.SkillName == "Perception");

        Assert.NotNull(athletics);
        Assert.Equal(ProficiencyLevel.Proficient, athletics!.ProficiencyLevel);
        Assert.NotNull(stealth);
        Assert.Equal(ProficiencyLevel.Proficient, stealth!.ProficiencyLevel);
        Assert.NotNull(perception);
        Assert.Equal(ProficiencyLevel.Proficient, perception!.ProficiencyLevel);
    }

    [Fact]
    public void MapToCanonical_Skills_Expert_AreMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nStealth: Expert\nAcrobatics: E\n");

        Assert.NotNull(result.MappedCharacter);
        var skills = result.MappedCharacter!.Skills;

        var stealth = skills.FirstOrDefault(s => s.SkillName == "Stealth");
        var acrobatics = skills.FirstOrDefault(s => s.SkillName == "Acrobatics");

        Assert.NotNull(stealth);
        Assert.Equal(ProficiencyLevel.Expert, stealth!.ProficiencyLevel);
        Assert.NotNull(acrobatics);
        Assert.Equal(ProficiencyLevel.Expert, acrobatics!.ProficiencyLevel);
    }

    [Fact]
    public void MapToCanonical_Skills_NotProficient_AreMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nArcana: No\nHistory: false\n");

        Assert.NotNull(result.MappedCharacter);
        var skills = result.MappedCharacter!.Skills;

        var arcana = skills.FirstOrDefault(s => s.SkillName == "Arcana");
        var history = skills.FirstOrDefault(s => s.SkillName == "History");

        Assert.NotNull(arcana);
        Assert.Equal(ProficiencyLevel.None, arcana!.ProficiencyLevel);
        Assert.NotNull(history);
        Assert.Equal(ProficiencyLevel.None, history!.ProficiencyLevel);
    }

    [Fact]
    public void MapToCanonical_SleightOfHand_DisplayNameIsCorrect()
    {
        var result = ExtractFromText("CharacterName: Thorn\nSleightOfHand: Yes\n");

        Assert.NotNull(result.MappedCharacter);
        var skill = result.MappedCharacter!.Skills.FirstOrDefault(s => s.SkillName == "Sleight of Hand");
        Assert.NotNull(skill);
        Assert.Equal(ProficiencyLevel.Proficient, skill!.ProficiencyLevel);
    }

    [Fact]
    public void MapToCanonical_AnimalHandling_DisplayNameIsCorrect()
    {
        var result = ExtractFromText("CharacterName: Thorn\nAnimalHandling: Yes\n");

        Assert.NotNull(result.MappedCharacter);
        var skill = result.MappedCharacter!.Skills.FirstOrDefault(s => s.SkillName == "Animal Handling");
        Assert.NotNull(skill);
    }

    [Fact]
    public void MapToCanonical_MulticlassCharacter_BothClassesMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nClassLevel: Fighter 5 / Rogue 3\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(2, result.MappedCharacter!.Classes.Count);
        Assert.Equal("Fighter", result.MappedCharacter.Classes[0].ClassName);
        Assert.Equal(5, result.MappedCharacter.Classes[0].Level);
        Assert.Equal("Rogue", result.MappedCharacter.Classes[1].ClassName);
        Assert.Equal(3, result.MappedCharacter.Classes[1].Level);
    }

    [Fact]
    public void MapToCanonical_KnownHitDie_IsCorrect()
    {
        var result = ExtractFromText("CharacterName: Thorn\nClassLevel: Barbarian 4\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(12, result.MappedCharacter!.Classes[0].HitDie);
    }

    [Fact]
    public void MapToCanonical_WizardHitDie_IsCorrect()
    {
        var result = ExtractFromText("CharacterName: Thorn\nClassLevel: Wizard 3\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(6, result.MappedCharacter!.Classes[0].HitDie);
    }

    [Fact]
    public void MapToCanonical_Alignment_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nAlignment: Chaotic Good\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal("Chaotic Good", result.MappedCharacter!.Identity.Alignment);
    }

    [Fact]
    public void MapToCanonical_Initiative_IsMapped()
    {
        var result = ExtractFromText("CharacterName: Thorn\nInitiative: 3\n");

        Assert.NotNull(result.MappedCharacter);
        Assert.Equal(3, result.MappedCharacter!.Combat.Initiative);
    }

    [Fact]
    public void MapToCanonical_FullCharacterSheet_AllFieldsMapped()
    {
        // Simulate a complete text-layer character sheet
        const string sheet = """
            CharacterName: Aldric Stormhand
            Race: Human
            Background: Soldier
            Alignment: Lawful Good
            ClassLevel: Fighter 5
            STR: 18
            DEX: 14
            CON: 16
            INT: 10
            WIS: 12
            CHA: 8
            AC: 18
            HPMax: 52
            Speed: 30
            ProficiencyBonus: 3
            StrSave: Yes
            ConSave: Yes
            Athletics: Yes
            Perception: Yes
            """;

        var result = ExtractFromText(sheet);

        Assert.NotNull(result.MappedCharacter);
        var c = result.MappedCharacter!;

        Assert.Equal("Aldric Stormhand", c.Identity.Name);
        Assert.Equal("Human", c.Identity.RaceOrAncestry);
        Assert.Equal("Soldier", c.Identity.Background);
        Assert.Equal("Lawful Good", c.Identity.Alignment);
        Assert.Single(c.Classes);
        Assert.Equal("Fighter", c.Classes[0].ClassName);
        Assert.Equal(5, c.Classes[0].Level);
        Assert.Equal(18, c.Abilities.Strength);
        Assert.Equal(14, c.Abilities.Dexterity);
        Assert.Equal(16, c.Abilities.Constitution);
        Assert.Equal(10, c.Abilities.Intelligence);
        Assert.Equal(12, c.Abilities.Wisdom);
        Assert.Equal(8, c.Abilities.Charisma);
        Assert.Equal(18, c.Combat.ArmorClass);
        Assert.Equal(52, c.Combat.MaxHitPoints);
        Assert.Equal(30, c.Combat.Speed);
        Assert.Equal(3, c.Combat.ProficiencyBonus);
        Assert.True(c.SavingThrows.Strength);
        Assert.True(c.SavingThrows.Constitution);
        Assert.False(c.SavingThrows.Dexterity);
        Assert.Contains(c.Skills, s => s.SkillName == "Athletics" && s.ProficiencyLevel == ProficiencyLevel.Proficient);
        Assert.Contains(c.Skills, s => s.SkillName == "Perception" && s.ProficiencyLevel == ProficiencyLevel.Proficient);
        Assert.Empty(result.UnmappedFields);
        Assert.False(result.IsOcrRequired);
    }

    // ── Helper: build a result from raw text ──────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PdfCharacterExtractionResult"/> by creating a real PDF
    /// with the given text content embedded as a page content stream, then running
    /// the extractor against it.
    ///
    /// This exercises the full text-extraction and mapping pipeline without mocking.
    /// </summary>
    private PdfCharacterExtractionResult ExtractFromText(string labelValueText)
    {
        var pdfBytes = BuildTextPdf(labelValueText);
        using var stream = new MemoryStream(pdfBytes);
        return _extractor.ExtractAsync(stream).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds a PDF byte array using PdfPig's document builder.
    /// Each non-empty line of <paramref name="labelValueText"/> is written as a
    /// separate text element at a distinct Y position so PdfPig extracts them
    /// as separate words/lines.
    /// </summary>
    private static byte[] BuildTextPdf(string labelValueText)
    {
        var lines = labelValueText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);

        // Write each line at a different Y position (top-down, 20pt spacing)
        double y = 800.0;
        const double lineHeight = 20.0;
        const double x = 50.0;
        const double fontSize = 10.0;

        foreach (var line in lines)
        {
            page.AddText(line, fontSize, new UglyToad.PdfPig.Core.PdfPoint(x, y), font);
            y -= lineHeight;
        }

        return builder.Build();
    }
}
