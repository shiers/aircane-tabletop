using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Aircane.Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Tests for <see cref="PdfPigTextExtractor.NormaliseText"/>: collapsing spaced-out heading
/// characters (PDF text layer only), then the camelCase and sentence-end splits.
/// </summary>
public class PdfTextNormalisationTests
{
    // ── Unit tests: NormaliseText ─────────────────────────────────────────────

    [Fact]
    public void NormaliseText_SpacedHeading_CollapsesToWord()
    {
        Assert.Equal("Actor", PdfPigTextExtractor.NormaliseText("A c t o r"));
    }

    [Fact]
    public void NormaliseText_SpacedMultiWord_CollapsesEachWord()
    {
        // Letter spacing makes the word gap indistinguishable from the letter gaps, so the whole
        // heading collapses into one run ("AbilityScores"); the camelCase step then restores the
        // break at the lower-to-upper boundary.
        Assert.Equal("Ability Scores", PdfPigTextExtractor.NormaliseText("A b i l i t y S c o r e s"));
    }

    [Fact]
    public void NormaliseText_SpacedHeadingInsideSentence_OnlyHeadingIsCollapsed()
    {
        Assert.Equal(
            "See Dexterity for details.",
            PdfPigTextExtractor.NormaliseText("See D e x t e r i t y for details."));
    }

    [Fact]
    public void NormaliseText_ShortSpacedPair_NotCollapsed()
    {
        // Two single letters is not a spaced-out word.
        Assert.Equal("a b", PdfPigTextExtractor.NormaliseText("a b"));
    }

    [Fact]
    public void NormaliseText_ThreeSingleLetters_AreCollapsed_KnownTradeOff()
    {
        // The pattern cannot tell a spaced-out word from three genuine single-letter tokens.
        // That is the accepted cost of the 3-letter minimum.
        Assert.Equal("xyz", PdfPigTextExtractor.NormaliseText("x y z"));
    }

    [Fact]
    public void NormaliseText_NormalText_Unchanged()
    {
        const string text = "Skilled at mimicry and dramatics";

        Assert.Equal(text, PdfPigTextExtractor.NormaliseText(text));
    }

    [Fact]
    public void NormaliseText_LettersInsideWords_NotCollapsed()
    {
        // Runs of single letters that touch a longer word are not spaced-out headings.
        const string text = "Roll a d20 or a d6 plus x2 at level 3";

        Assert.Equal(text, PdfPigTextExtractor.NormaliseText(text));
    }

    [Fact]
    public void NormaliseText_CamelCaseSplitStillApplies()
    {
        Assert.Equal("Actor Skilled", PdfPigTextExtractor.NormaliseText("ActorSkilled"));
    }

    [Fact]
    public void NormaliseText_SentenceEndSplitStillApplies()
    {
        Assert.Equal("dramatics. Actor", PdfPigTextExtractor.NormaliseText("dramatics.Actor"));
    }

    [Fact]
    public void NormaliseText_AllThreeStepsCombine()
    {
        Assert.Equal(
            "10 feet. Actor Skilled at mimicry",
            PdfPigTextExtractor.NormaliseText("10 feet.A c t o r Skilled at mimicry"));
    }

    [Fact]
    public void NormaliseText_OcrOutput_SkipsSpacedCollapseButKeepsOtherSteps()
    {
        Assert.Equal("A c t o r", PdfPigTextExtractor.NormaliseText("A c t o r", isOcrOutput: true));
        Assert.Equal("10 feet. Actor Skilled", PdfPigTextExtractor.NormaliseText("10 feet.ActorSkilled", isOcrOutput: true));
    }

    // ── End to end through the extractor ──────────────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_TextLayerWithSpacedHeading_IsCollapsed()
    {
        var extractor = CreateExtractor(new StubOcrEngine("unused"));
        using var pdf = BuildPdf("(A c t o r) Tj");

        var result = await extractor.ExtractTextAsync(pdf);

        Assert.False(result.IsOcrRequired);
        Assert.Equal("Actor", Assert.Single(result.Pages).Text);
    }

    [Fact]
    public async Task ExtractTextAsync_TextLayerWithRunTogetherText_IsSplit()
    {
        var extractor = CreateExtractor(new StubOcrEngine("unused"));
        using var pdf = BuildPdf("(10 feet.ActorSkilled at mimicry) Tj");

        var result = await extractor.ExtractTextAsync(pdf);

        Assert.Equal("10 feet. Actor Skilled at mimicry", Assert.Single(result.Pages).Text);
    }

    [Fact]
    public async Task NormaliseText_SpacedCollapseDoesNotRunOnOcrOutput()
    {
        // A page with no text layer, so the (stub) Tesseract path supplies the text.
        var extractor = CreateExtractor(new StubOcrEngine("A c t o r"), rasterizer: new StubRasterizer());
        using var pdf = BuildPdf(contentStream: null);

        var result = await extractor.ExtractTextAsync(pdf);

        Assert.Equal("A c t o r", Assert.Single(result.Pages).Text);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PdfPigTextExtractor CreateExtractor(IOcrEngine ocr, IPdfRasterizer? rasterizer = null) =>
        new(NullLogger<PdfPigTextExtractor>.Instance,
            ocr,
            ocrThresholdCharsPerPage: 1,
            rasterizer,
            new OcrOptions { Enabled = true, FullPageRasterization = rasterizer is not null });

    /// <summary>
    /// Builds a one-page PDF with a correct xref table. <paramref name="textOperators"/> are placed
    /// inside a BT/ET block using Helvetica; null produces a page with no content (no text layer).
    /// </summary>
    private static MemoryStream BuildPdf(string? textOperators = null, string? contentStream = "")
    {
        var content = textOperators is null
            ? null
            : $"BT /F1 12 Tf 72 700 Td {textOperators} ET";

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            content is null
                ? "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"
                : "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                  "/Resources << /Font << /F1 5 0 R >> >> >>",
            content is null
                ? "<< >>"
                : $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefStart = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");

        return new MemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    private sealed class StubOcrEngine(string text) : IOcrEngine
    {
        public bool IsAvailable => true;
        public string StatusDescription => "Stub OCR engine (test).";

        public Task<OcrResult> RecognizeAsync(
            byte[] imageBytes,
            float? minConfidenceOverride = null,
            OcrSegmentationMode segmentationMode = OcrSegmentationMode.Default,
            CancellationToken ct = default) =>
            Task.FromResult(new OcrResult(text, 0.95f));
    }

    private sealed class StubRasterizer : IPdfRasterizer
    {
        public bool IsAvailable => true;

        public byte[]? RasterizePage(byte[] pdfBytes, int pageNumber, CancellationToken ct = default) =>
            [0x42, 0x4D, 0x01, 0x02];
    }
}
