using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// A no-op OCR engine used when OCR is disabled by configuration. Always reports
/// <see cref="IsAvailable"/> as <c>false</c> and returns <see cref="OcrResult.Empty"/>,
/// so the import pipeline falls back to marking scanned PDFs as OCR-required.
/// </summary>
public sealed class NullOcrEngine : IOcrEngine
{
    public bool IsAvailable => false;

    public string StatusDescription => "OCR is disabled (set Ocr:Enabled=true to enable).";

    public Task<OcrResult> RecognizeAsync(
        byte[] imageBytes, float? minConfidenceOverride = null, CancellationToken ct = default)
        => Task.FromResult(OcrResult.Empty);
}
