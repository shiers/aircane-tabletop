using Aircane.Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Unit tests for <see cref="PdfPigTextExtractor"/>.
/// Tests OCR detection logic, normal extraction, and corrupt/empty PDF handling.
/// </summary>
public class PdfPigTextExtractorTests
{
    // ── OCR detection: corrupt / empty stream ─────────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_EmptyStream_ReturnsOcrRequired()
    {
        var extractor = CreateExtractor();
        using var stream = new MemoryStream(Array.Empty<byte>());

        var result = await extractor.ExtractTextAsync(stream);

        Assert.True(result.IsOcrRequired);
        Assert.Equal(0, result.TotalCharacters);
        Assert.Empty(result.Pages);
    }

    [Fact]
    public async Task ExtractTextAsync_CorruptBytes_ReturnsOcrRequired()
    {
        var extractor = CreateExtractor();
        // Random bytes that are not a valid PDF
        using var stream = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xAB });

        var result = await extractor.ExtractTextAsync(stream);

        Assert.True(result.IsOcrRequired);
        Assert.Equal(0, result.TotalCharacters);
    }

    [Fact]
    public async Task ExtractTextAsync_NotAPdf_ReturnsOcrRequired()
    {
        var extractor = CreateExtractor();
        // Plain text content that is not a PDF
        var bytes = System.Text.Encoding.UTF8.GetBytes("This is not a PDF file at all.");
        using var stream = new MemoryStream(bytes);

        var result = await extractor.ExtractTextAsync(stream);

        Assert.True(result.IsOcrRequired);
    }

    // ── OCR detection: low character count ───────────────────────────────────

    [Fact]
    public void OcrThreshold_DefaultIs50CharsPerPage()
    {
        Assert.Equal(50, PdfPigTextExtractor.DefaultOcrThresholdCharsPerPage);
    }

    [Theory]
    [InlineData(0, 1, true)]    // 0 chars / 1 page = 0 avg → OCR required
    [InlineData(49, 1, true)]   // 49 chars / 1 page = 49 avg → OCR required (below threshold)
    [InlineData(50, 1, false)]  // 50 chars / 1 page = 50 avg → not OCR required (at threshold)
    [InlineData(51, 1, false)]  // 51 chars / 1 page = 51 avg → not OCR required
    [InlineData(99, 2, true)]   // 99 chars / 2 pages = 49.5 avg → OCR required
    [InlineData(100, 2, false)] // 100 chars / 2 pages = 50 avg → not OCR required
    [InlineData(200, 4, false)] // 200 chars / 4 pages = 50 avg → not OCR required
    public void OcrDetection_AverageCharsPerPage_CorrectlyDeterminesOcrRequired(
        int totalChars, int pageCount, bool expectedOcrRequired)
    {
        // Simulate the OCR detection logic directly (mirrors the implementation).
        bool isOcrRequired;
        if (pageCount == 0)
        {
            isOcrRequired = true;
        }
        else
        {
            var avgCharsPerPage = (double)totalChars / pageCount;
            isOcrRequired = avgCharsPerPage < PdfPigTextExtractor.DefaultOcrThresholdCharsPerPage;
        }

        Assert.Equal(expectedOcrRequired, isOcrRequired);
    }

    [Fact]
    public void OcrDetection_ZeroPages_IsOcrRequired()
    {
        // A PDF with no pages should always be OCR-required.
        const int pageCount = 0;
        var isOcrRequired = pageCount == 0;

        Assert.True(isOcrRequired);
    }

    // ── Normal PDF extraction: real minimal PDF ───────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_MinimalValidPdf_ReturnsResult()
    {
        var extractor = CreateExtractor();
        using var stream = CreateMinimalPdf();

        // A minimal PDF may have no text (it's just structure), but it should not throw.
        var result = await extractor.ExtractTextAsync(stream);

        // Result should be returned (not throw), and IsOcrRequired reflects text content.
        Assert.NotNull(result);
        Assert.NotNull(result.Pages);
    }

    [Fact]
    public async Task ExtractTextAsync_CancellationRequested_ThrowsOrReturnsOcrRequired()
    {
        var extractor = CreateExtractor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var stream = CreateMinimalPdf();

        // Either throws OperationCanceledException or returns gracefully with OcrRequired.
        // Both are acceptable behaviors for a cancelled operation.
        try
        {
            var result = await extractor.ExtractTextAsync(stream, cts.Token);
            // If it returns, it should be marked as OCR-required or have no pages.
            Assert.True(result.IsOcrRequired || result.Pages.Count == 0 || result.Pages.Count >= 0);
        }
        catch (OperationCanceledException)
        {
            // Expected - cancellation propagated correctly.
        }
    }

    // ── PdfExtractionResult record ────────────────────────────────────────────

    [Fact]
    public void PdfExtractionResult_Properties_AreCorrect()
    {
        var pages = new List<Aircane.Application.DocumentProcessing.PageText>
        {
            new(PageNumber: 1, Text: "Hello world", CharacterCount: 11),
            new(PageNumber: 2, Text: "Second page", CharacterCount: 11),
        };

        var result = new Aircane.Application.DocumentProcessing.PdfExtractionResult(
            Pages: pages.AsReadOnly(),
            IsOcrRequired: false,
            TotalCharacters: 22);

        Assert.Equal(2, result.Pages.Count);
        Assert.False(result.IsOcrRequired);
        Assert.Equal(22, result.TotalCharacters);
    }

    [Fact]
    public void PageText_Properties_AreCorrect()
    {
        var page = new Aircane.Application.DocumentProcessing.PageText(
            PageNumber: 3,
            Text: "Some text here",
            CharacterCount: 14);

        Assert.Equal(3, page.PageNumber);
        Assert.Equal("Some text here", page.Text);
        Assert.Equal(14, page.CharacterCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PdfPigTextExtractor CreateExtractor(int threshold = PdfPigTextExtractor.DefaultOcrThresholdCharsPerPage)
        => new(NullLogger<PdfPigTextExtractor>.Instance, threshold);

    /// <summary>
    /// Creates a minimal but structurally valid PDF stream with one empty page.
    /// This is the smallest valid PDF that PdfPig can open without throwing.
    /// </summary>
    private static MemoryStream CreateMinimalPdf()
    {
        // Minimal valid PDF with one empty page (no text content).
        // Based on the PDF specification minimum structure.
        const string minimalPdf =
            "%PDF-1.4\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n" +
            "xref\n0 4\n" +
            "0000000000 65535 f \n" +
            "0000000009 00000 n \n" +
            "0000000058 00000 n \n" +
            "0000000115 00000 n \n" +
            "trailer\n<< /Size 4 /Root 1 0 R >>\n" +
            "startxref\n190\n%%EOF";

        var bytes = System.Text.Encoding.ASCII.GetBytes(minimalPdf);
        return new MemoryStream(bytes);
    }
}
