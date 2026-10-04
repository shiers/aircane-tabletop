using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Characters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// End-to-end tests for the D&amp;D Beyond printable-sheet OCR import path through
/// <c>POST /api/characters/import/pdf</c>. CI-hermetic: a synthetic DDB-style PDF (template captions
/// only, SYNTHETIC values) is driven through a STUB <see cref="ICaptionRegionOcr"/> so no native
/// Tesseract/PDFium is required. No real PDF or personal values are used (NFR-4).
/// </summary>
public sealed class DndBeyondPdfOcrImportTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly AircaneWebApplicationFactory _factory;

    public DndBeyondPdfOcrImportTests(AircaneWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Stub ICaptionRegionOcr: returns synthetic text keyed by region (caption) key ──────────

    private sealed class StubCaptionRegionOcr : ICaptionRegionOcr
    {
        private readonly IReadOnlyDictionary<string, string> _byCaption;
        public StubCaptionRegionOcr(IReadOnlyDictionary<string, string> byCaption) => _byCaption = byCaption;

        public bool IsAvailable => true;

        public Task<IReadOnlyList<RegionOcrResult>> RecognizeRegionsAsync(
            byte[] pdfBytes, int pageNumber, IReadOnlyCollection<OcrRegion> regions, CancellationToken ct = default)
        {
            var results = regions
                .Select(r => new RegionOcrResult(
                    r.Key,
                    _byCaption.TryGetValue(r.Key, out var text) ? text : string.Empty,
                    0.9f))
                .ToList();
            return Task.FromResult<IReadOnlyList<RegionOcrResult>>(results);
        }
    }

    private HttpClient CreateClient(ICaptionRegionOcr? captionOcr)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(ICaptionRegionOcr));
                if (existing is not null)
                    services.Remove(existing);

                if (captionOcr is not null)
                    services.AddSingleton(captionOcr);
                // When null: leave ICaptionRegionOcr unregistered → extractor sees it unavailable.
            });
        }).CreateClient();
    }

    // ── Synthetic DDB-style PDF: template captions only, positioned like the real sheet ────────

    private static byte[] BuildSyntheticDdbSheet(bool useSpecies)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        // Place template CAPTIONS as bare words at distinct positions (PDF coords, Y from bottom).
        // These are template text, not PII. No values are placed in the text layer — the values are
        // "rasterized" and only recovered through the stub OCR engine.
        void Caption(string text, double x, double yFromTop) =>
            page.AddText(text, 9, new PdfPoint(x, 842 - yFromTop), font);

        // Header row.
        Caption("CLASS & LEVEL", 40, 40);
        Caption("CHARACTER NAME", 120, 70);
        Caption(useSpecies ? "SPECIES" : "RACE", 260, 70);
        Caption("BACKGROUND", 360, 70);

        // Stat row.
        Caption("ARMOR", 120, 120);
        Caption("HIT POINTS", 200, 120);
        Caption("PROFICIENCY BONUS", 300, 120);
        Caption("SPEED", 440, 120);

        // Ability captions column.
        Caption("STRENGTH", 40, 200);
        Caption("DEXTERITY", 40, 260);
        Caption("CONSTITUTION", 40, 320);
        Caption("INTELLIGENCE", 40, 380);
        Caption("WISDOM", 40, 440);
        Caption("CHARISMA", 40, 500);

        // Signature corroboration.
        Caption("PASSIVE PERCEPTION", 300, 560);

        return builder.Build();
    }

    private static IReadOnlyDictionary<string, string> SyntheticRegionText(
        string strength = "16",
        string hitPoints = "25") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["STRENGTH"] = strength,
        ["DEXTERITY"] = "14",
        ["CONSTITUTION"] = "13",
        ["INTELLIGENCE"] = "12",
        ["WISDOM"] = "10",
        ["CHARISMA"] = "8",
        ["ARMOR"] = "17",
        ["HIT POINTS"] = hitPoints,
        ["SPEED"] = "30 ft.",
        ["PROFICIENCY BONUS"] = "3",
        ["CHARACTER NAME"] = "Test Character",
        ["CLASS & LEVEL"] = "Fighter 5",
        ["SPECIES"] = "Elf",
        ["RACE"] = "Human",
        ["BACKGROUND"] = "Sage",
    };

    private static MultipartFormDataContent BuildForm(byte[] pdf, string ruleset = "2014")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdf);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "synthetic-ddb.pdf");
        content.Add(new StringContent("dnd5e"), "gameSystem");
        content.Add(new StringContent(ruleset), "ruleset");
        return content;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_SyntheticDdbSheet_RoutesThroughOcr_MapsExpectedValues()
    {
        var client = CreateClient(new StubCaptionRegionOcr(SyntheticRegionText()));
        var pdf = BuildSyntheticDdbSheet(useSpecies: true);

        var response = await client.PostAsync("/api/characters/import/pdf", BuildForm(pdf));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = await response.Content.ReadFromJsonAsync<CharacterFieldReviewDto>();
        Assert.NotNull(review);
        Assert.True(review!.ReviewRequired);
        Assert.NotEqual(Guid.Empty, review.CharacterId);

        // The persisted draft carries the OCR'd values; fetch it and assert the canonical fields.
        var character = await client.GetFromJsonAsync<CharacterDto>($"/api/characters/{review.CharacterId}");
        Assert.NotNull(character);
        var canonical = System.Text.Json.JsonDocument.Parse(character!.CanonicalJson).RootElement;

        var abilities = canonical.GetProperty("abilities");
        Assert.Equal(16, abilities.GetProperty("strength").GetInt32());
        Assert.Equal(14, abilities.GetProperty("dexterity").GetInt32());

        var combat = canonical.GetProperty("combat");
        Assert.Equal(17, combat.GetProperty("armorClass").GetInt32());
        Assert.Equal(25, combat.GetProperty("maxHitPoints").GetInt32());
    }

    [Fact]
    public async Task Import_SpeciesFixture_DetectedAs2024_OverridesFormRuleset()
    {
        var client = CreateClient(new StubCaptionRegionOcr(SyntheticRegionText()));
        var pdf = BuildSyntheticDdbSheet(useSpecies: true);

        var response = await client.PostAsync("/api/characters/import/pdf", BuildForm(pdf, ruleset: "2014"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = await response.Content.ReadFromJsonAsync<CharacterFieldReviewDto>();
        var character = await client.GetFromJsonAsync<CharacterDto>($"/api/characters/{review!.CharacterId}");

        // Detected SPECIES ⇒ 2024 OVERRIDES the form "2014".
        Assert.Equal("2024", character!.Ruleset);
    }

    [Fact]
    public async Task Import_RaceFixture_DetectedAs2014()
    {
        var client = CreateClient(new StubCaptionRegionOcr(SyntheticRegionText()));
        var pdf = BuildSyntheticDdbSheet(useSpecies: false);

        var response = await client.PostAsync("/api/characters/import/pdf", BuildForm(pdf, ruleset: "2014"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = await response.Content.ReadFromJsonAsync<CharacterFieldReviewDto>();
        var character = await client.GetFromJsonAsync<CharacterDto>($"/api/characters/{review!.CharacterId}");

        Assert.Equal("2014", character!.Ruleset);
    }

    [Fact]
    public async Task Import_EveryOcrDerivedField_IsFlaggedForReview()
    {
        var client = CreateClient(new StubCaptionRegionOcr(SyntheticRegionText()));
        var pdf = BuildSyntheticDdbSheet(useSpecies: true);

        var response = await client.PostAsync("/api/characters/import/pdf", BuildForm(pdf));
        var review = await response.Content.ReadFromJsonAsync<CharacterFieldReviewDto>();

        var flaggedPaths = review!.UnmappedFields
            .Where(f => f.RequiresReview)
            .Select(f => f.SuggestedCanonicalField)
            .ToList();

        Assert.Contains("abilities.strength", flaggedPaths);
        Assert.Contains("combat.armorClass", flaggedPaths);
        Assert.Contains("combat.maxHitPoints", flaggedPaths);
        Assert.Contains("combat.speed", flaggedPaths);
        Assert.Contains("combat.proficiencyBonus", flaggedPaths);
        Assert.Contains("identity.name", flaggedPaths);
        Assert.Contains("identity.background", flaggedPaths);
    }

    [Fact]
    public async Task Import_OutOfRangeOcrValues_ClampedAndFlagged_PersistAsReviewDraft()
    {
        // Synthetic out-of-range OCR values: STR 88, HP -4. The sanitizer clamps and flags them and
        // the draft still persists (acc.9).
        var client = CreateClient(new StubCaptionRegionOcr(SyntheticRegionText(strength: "88", hitPoints: "-4")));
        var pdf = BuildSyntheticDdbSheet(useSpecies: true);

        var response = await client.PostAsync("/api/characters/import/pdf", BuildForm(pdf));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = await response.Content.ReadFromJsonAsync<CharacterFieldReviewDto>();
        var character = await client.GetFromJsonAsync<CharacterDto>($"/api/characters/{review!.CharacterId}");
        var canonical = System.Text.Json.JsonDocument.Parse(character!.CanonicalJson).RootElement;

        // STR 88 clamped to 30; MaxHitPoints -4 floored to 0.
        Assert.Equal(30, canonical.GetProperty("abilities").GetProperty("strength").GetInt32());
        Assert.Equal(0, canonical.GetProperty("combat").GetProperty("maxHitPoints").GetInt32());

        // STR flagged for review (via the OCR path and/or the sanitizer).
        Assert.Contains(review.UnmappedFields, f => f.SuggestedCanonicalField == "abilities.strength" && f.RequiresReview);
    }

    [Fact]
    public async Task Import_OcrUnavailable_ReturnsActionable422_NotOldWording()
    {
        // No ICaptionRegionOcr registered → extractor sees OCR unavailable for a detected DDB sheet.
        var client = CreateClient(captionOcr: null);
        var pdf = BuildSyntheticDdbSheet(useSpecies: true);

        var response = await client.PostAsync("/api/characters/import/pdf", BuildForm(pdf));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("not available in the current version", body);
        Assert.Contains("D&D Beyond", body);
    }
}
