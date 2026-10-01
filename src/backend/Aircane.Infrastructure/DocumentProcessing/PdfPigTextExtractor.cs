using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// Extracts text from PDF files using UglyToad.PdfPig.
/// Detects low/no-text PDFs and, when an OCR engine is available, attempts to recover
/// text from the page's embedded raster images before marking the document OCR-required.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <summary>
    /// Average characters per page below this threshold triggers OCR-required detection.
    /// </summary>
    public const int DefaultOcrThresholdCharsPerPage = 50;

    private readonly int _ocrThresholdCharsPerPage;
    private readonly Aircane.Application.Abstractions.IOcrEngine _ocrEngine;
    private readonly Aircane.Application.Abstractions.IPdfRasterizer? _rasterizer;
    private readonly OcrOptions _ocrOptions;
    private readonly ILogger<PdfPigTextExtractor> _logger;

    public PdfPigTextExtractor(
        ILogger<PdfPigTextExtractor> logger,
        Aircane.Application.Abstractions.IOcrEngine ocrEngine,
        int ocrThresholdCharsPerPage = DefaultOcrThresholdCharsPerPage,
        Aircane.Application.Abstractions.IPdfRasterizer? rasterizer = null,
        OcrOptions? ocrOptions = null)
    {
        _logger = logger;
        _ocrEngine = ocrEngine;
        _ocrThresholdCharsPerPage = ocrThresholdCharsPerPage;
        _rasterizer = rasterizer;
        _ocrOptions = ocrOptions ?? new OcrOptions();
    }

    /// <inheritdoc />
    public async Task<PdfExtractionResult> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default)
    {
        // Buffer the stream so we can both parse it (PdfPig) and, if needed, hand the raw bytes to
        // the full-page rasterizer. The buffer is only materialized when rasterization is enabled;
        // otherwise the original stream is parsed directly.
        byte[]? pdfBytes = null;
        List<PdfPage> parsedPages;
        try
        {
            if (FullPageRasterizationEnabled)
            {
                using var buffer = new MemoryStream();
                await pdfStream.CopyToAsync(buffer, ct);
                pdfBytes = buffer.ToArray();
                using var parseStream = new MemoryStream(pdfBytes, writable: false);
                parsedPages = ParsePages(parseStream, ct);
            }
            else
            {
                parsedPages = ParsePages(pdfStream, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open or parse PDF stream. Marking as OCR-required.");
            return new PdfExtractionResult(
                Pages: Array.Empty<PageText>(),
                IsOcrRequired: true,
                TotalCharacters: 0);
        }

        var pageTexts = parsedPages
            .Select(p => new PageText(p.PageNumber, p.Text, p.Text.Length))
            .ToList();

        var pageCount = pageTexts.Count;
        var totalChars = pageTexts.Sum(p => p.CharacterCount);

        if (!IsBelowTextThreshold(pageCount, totalChars))
        {
            // Enough native text; no OCR needed.
            return new PdfExtractionResult(pageTexts.AsReadOnly(), IsOcrRequired: false, totalChars);
        }

        // Low/no extractable text. Try OCR when an engine is available.
        if (!_ocrEngine.IsAvailable)
        {
            _logger.LogInformation(
                "PDF has {PageCount} pages with {TotalChars} total characters and OCR is unavailable " +
                "({OcrStatus}). Marking as OCR-required.",
                pageCount, totalChars, _ocrEngine.StatusDescription);
            return new PdfExtractionResult(pageTexts.AsReadOnly(), IsOcrRequired: true, totalChars);
        }

        _logger.LogInformation(
            "PDF has {PageCount} pages with {TotalChars} total characters (below OCR threshold). " +
            "Attempting OCR via {OcrStatus}.",
            pageCount, totalChars, _ocrEngine.StatusDescription);

        var ocredPages = await RunOcrAsync(parsedPages, pageTexts, pdfBytes, ct);

        var ocrTotalChars = ocredPages.Sum(p => p.CharacterCount);
        var stillOcrRequired = IsBelowTextThreshold(ocredPages.Count, ocrTotalChars);

        if (stillOcrRequired)
        {
            _logger.LogInformation(
                "OCR produced insufficient text ({OcrChars} chars across {PageCount} pages). " +
                "Marking as OCR-required.",
                ocrTotalChars, ocredPages.Count);
        }
        else
        {
            _logger.LogInformation(
                "OCR recovered {OcrChars} characters across {PageCount} pages.",
                ocrTotalChars, ocredPages.Count);
        }

        return new PdfExtractionResult(ocredPages.AsReadOnly(), stillOcrRequired, ocrTotalChars);
    }

    private async Task<List<PageText>> RunOcrAsync(
        List<PdfPage> parsedPages,
        List<PageText> nativePageTexts,
        byte[]? pdfBytes,
        CancellationToken ct)
    {
        var result = new List<PageText>(parsedPages.Count);

        for (int i = 0; i < parsedPages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var parsed = parsedPages[i];
            var nativeText = nativePageTexts[i].Text;

            // If this individual page already has enough text, keep the native text.
            if (nativeText.Length >= _ocrThresholdCharsPerPage)
            {
                result.Add(new PageText(parsed.PageNumber, nativeText, nativeText.Length));
                continue;
            }

            var recognized = await RecognizePageAsync(parsed, pdfBytes, ct);

            // Prefer whichever source yielded more text so we never lose native text.
            var best = recognized.Length > nativeText.Length ? recognized : nativeText;
            result.Add(new PageText(parsed.PageNumber, best, best.Length));
        }

        return result;
    }

    private async Task<string> RecognizePageAsync(PdfPage page, byte[]? pdfBytes, CancellationToken ct)
    {
        var recognizedParts = new List<string>();

        // First, OCR any embedded raster images on the page.
        foreach (var imageBytes in page.Images)
        {
            ct.ThrowIfCancellationRequested();

            var ocr = await _ocrEngine.RecognizeAsync(imageBytes, ct);
            if (ocr.HasText)
                recognizedParts.Add(ocr.Text.Trim());
        }

        // Full-page rasterization fallback: if the page had no embedded images (or they yielded
        // nothing) and rasterization is enabled, render the whole page and OCR the bitmap. This
        // recovers text from scanned pages drawn as vectors rather than embedded rasters.
        if (recognizedParts.Count == 0 &&
            FullPageRasterizationEnabled &&
            pdfBytes is not null &&
            _rasterizer is { IsAvailable: true })
        {
            ct.ThrowIfCancellationRequested();
            var bitmap = _rasterizer.RasterizePage(pdfBytes, page.PageNumber, ct);
            if (bitmap is { Length: > 0 })
            {
                var ocr = await _ocrEngine.RecognizeAsync(bitmap, ct);
                if (ocr.HasText)
                    recognizedParts.Add(ocr.Text.Trim());
            }
        }

        return string.Join("\n", recognizedParts);
    }

    /// <summary>True when full-page rasterization OCR is enabled and a rasterizer is wired in.</summary>
    private bool FullPageRasterizationEnabled =>
        _ocrOptions.Enabled && _ocrOptions.FullPageRasterization && _rasterizer is not null;

    private bool IsBelowTextThreshold(int pageCount, int totalChars)
    {
        if (pageCount == 0)
            return true;

        var avgCharsPerPage = (double)totalChars / pageCount;
        return avgCharsPerPage < _ocrThresholdCharsPerPage;
    }

    private static List<PdfPage> ParsePages(Stream pdfStream, CancellationToken ct)
    {
        var pages = new List<PdfPage>();

        using var document = PdfDocument.Open(pdfStream);

        foreach (Page page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var text = page.Text ?? string.Empty;

            // Collect embedded raster images so OCR can be attempted on scanned pages.
            // Only materialize image bytes when the page text is sparse to avoid the cost
            // on text-rich pages.
            var images = new List<byte[]>();
            if (text.Length < DefaultOcrThresholdCharsPerPage)
            {
                foreach (var img in page.GetImages())
                {
                    ct.ThrowIfCancellationRequested();
                    var bytes = TryGetImageBytes(img);
                    if (bytes is not null && bytes.Length > 0)
                        images.Add(bytes);
                }
            }

            pages.Add(new PdfPage(page.Number, text, images));
        }

        return pages;
    }

    private static byte[]? TryGetImageBytes(IPdfImage image)
    {
        try
        {
            // Prefer a losslessly decoded PNG when PdfPig can provide one.
            if (image.TryGetPng(out var png) && png is { Length: > 0 })
                return png;

            // Fall back to the raw encoded bytes (e.g. an embedded JPEG stream),
            // which Leptonica can typically decode directly.
            var raw = image.RawBytes.ToArray();
            if (raw.Length > 0)
                return raw;
        }
        catch
        {
            // Some images use filters PdfPig can't decode; skip them.
        }

        return null;
    }

    private sealed record PdfPage(int PageNumber, string Text, IReadOnlyList<byte[]> Images);
}
