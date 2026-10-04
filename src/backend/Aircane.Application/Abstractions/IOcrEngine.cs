namespace Aircane.Application.Abstractions;

/// <summary>
/// Optical character recognition engine that recognizes text from raster images.
/// </summary>
/// <remarks>
/// OCR depends on a native library (e.g. libtesseract) and language data files that
/// may not be present in every environment. Implementations MUST NOT throw during
/// construction when those dependencies are missing; instead they report
/// <see cref="IsAvailable"/> as <c>false</c> so callers can gate cleanly and fall back
/// to marking a document as OCR-required.
/// </remarks>
public interface IOcrEngine
{
    /// <summary>
    /// Whether the native OCR engine and its language data are usable in this environment.
    /// When <c>false</c>, callers should skip OCR and fall back gracefully.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A short human-readable description of why OCR is unavailable, or the engine
    /// identity when it is available. Useful for logs and status messages.
    /// </summary>
    string StatusDescription { get; }

    /// <summary>
    /// Recognizes text from a single raster image (PNG, JPEG, TIFF, or BMP bytes).
    /// </summary>
    /// <param name="imageBytes">Encoded image bytes for one page or region.</param>
    /// <param name="minConfidenceOverride">
    /// Optional per-call confidence floor (0..1). When <c>null</c> the engine uses its configured
    /// <c>OcrOptions.MinConfidence</c> page-level gate (unchanged document behaviour). The
    /// region-anchored consumer passes <c>OcrOptions.RegionMinConfidence</c> (default 0) so a
    /// low-confidence region value is kept-and-flagged by the consumer rather than silently dropped.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The recognized text, or an empty <see cref="OcrResult"/> when the engine is
    /// unavailable or nothing could be recognized. Implementations should not throw
    /// for recognition failures; they should return an empty result instead.
    /// </returns>
    Task<OcrResult> RecognizeAsync(
        byte[] imageBytes, float? minConfidenceOverride = null, CancellationToken ct = default);
}

/// <summary>
/// The outcome of an OCR recognition pass over a single image.
/// </summary>
/// <param name="Text">The recognized text (may be empty).</param>
/// <param name="MeanConfidence">
/// Mean confidence across recognized words in the range 0..1, or 0 when unknown.
/// </param>
public record OcrResult(string Text, float MeanConfidence)
{
    /// <summary>An empty result indicating nothing was recognized.</summary>
    public static readonly OcrResult Empty = new(string.Empty, 0f);

    /// <summary>Whether any non-whitespace text was recognized.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}
