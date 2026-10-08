namespace Aircane.Application.Abstractions;

/// <summary>A named rectangular region in normalized, TOP-DOWN page coordinates
/// (0..1 of page width/height; Y measured from the TOP of the page).</summary>
public readonly record struct OcrRegion(string Key, double X, double Y, double Width, double Height);

/// <summary>Recognized text for one named region.</summary>
public sealed record RegionOcrResult(string Key, string Text, float Confidence);

/// <summary>
/// Caption/region-anchored OCR: rasterizes a page (via <see cref="IPdfRasterizer"/>) and OCRs each
/// named region (via <see cref="IOcrEngine"/>), returning per-region text. Honours the never-throw
/// contract: when OCR or the rasterizer is unavailable, returns an empty result set and reports via
/// <see cref="IsAvailable"/>. No consumer-specific layout is baked in — callers supply the regions.
/// </summary>
public interface ICaptionRegionOcr
{
    /// <summary>
    /// Whether both the rasterizer and the OCR engine are usable in this environment. When
    /// <c>false</c>, <see cref="RecognizeRegionsAsync"/> returns an empty list.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Rasterizes the given 1-based page once and OCRs each supplied region, returning one
    /// <see cref="RegionOcrResult"/> per region (in the input order). An out-of-bounds, failed, or
    /// empty region yields <c>(key, "", 0f)</c>. Never throws; when unavailable returns an empty list.
    /// </summary>
    Task<IReadOnlyList<RegionOcrResult>> RecognizeRegionsAsync(
        byte[] pdfBytes, int pageNumber, IReadOnlyCollection<OcrRegion> regions, CancellationToken ct = default);
}
