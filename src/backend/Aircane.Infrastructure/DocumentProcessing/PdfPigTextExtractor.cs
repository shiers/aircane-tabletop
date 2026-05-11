using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// Extracts text from PDF files using UglyToad.PdfPig.
/// Detects low/no-text PDFs and marks them as OCR-required.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <summary>
    /// Average characters per page below this threshold triggers OCR-required detection.
    /// </summary>
    public const int DefaultOcrThresholdCharsPerPage = 50;

    private readonly int _ocrThresholdCharsPerPage;
    private readonly ILogger<PdfPigTextExtractor> _logger;

    public PdfPigTextExtractor(
        ILogger<PdfPigTextExtractor> logger,
        int ocrThresholdCharsPerPage = DefaultOcrThresholdCharsPerPage)
    {
        _logger = logger;
        _ocrThresholdCharsPerPage = ocrThresholdCharsPerPage;
    }

    /// <inheritdoc />
    public Task<PdfExtractionResult> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default)
    {
        // PdfPig is synchronous; extraction is CPU-bound so we run it inline.
        try
        {
            return Task.FromResult(ExtractInternal(pdfStream, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open or parse PDF stream. Marking as OCR-required.");
            return Task.FromResult(new PdfExtractionResult(
                Pages: Array.Empty<PageText>(),
                IsOcrRequired: true,
                TotalCharacters: 0));
        }
    }

    private PdfExtractionResult ExtractInternal(Stream pdfStream, CancellationToken ct)
    {
        var pages = new List<PageText>();

        using var document = PdfDocument.Open(pdfStream);

        foreach (Page page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var text = page.Text ?? string.Empty;
            pages.Add(new PageText(
                PageNumber: page.Number,
                Text: text,
                CharacterCount: text.Length));
        }

        var totalChars = pages.Sum(p => p.CharacterCount);
        var pageCount = pages.Count;

        bool isOcrRequired;
        if (pageCount == 0)
        {
            isOcrRequired = true;
        }
        else
        {
            var avgCharsPerPage = (double)totalChars / pageCount;
            isOcrRequired = avgCharsPerPage < _ocrThresholdCharsPerPage;
        }

        if (isOcrRequired)
        {
            _logger.LogInformation(
                "PDF has {PageCount} pages with {TotalChars} total characters " +
                "(avg {Avg:F1}/page). Marking as OCR-required.",
                pageCount,
                totalChars,
                pageCount > 0 ? (double)totalChars / pageCount : 0);
        }

        return new PdfExtractionResult(
            Pages: pages.AsReadOnly(),
            IsOcrRequired: isOcrRequired,
            TotalCharacters: totalChars);
    }
}
