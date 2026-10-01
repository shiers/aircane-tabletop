using Aircane.Application.Characters;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Extracts character data from a PDF stream.
/// Attempts form-field extraction first, falls back to text-layer extraction,
/// and marks the result as OCR-required when neither yields content.
/// </summary>
public interface IPdfCharacterExtractor
{
    /// <summary>
    /// Extracts character fields from the given PDF stream.
    /// </summary>
    /// <param name="pdfStream">A readable stream containing the PDF bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PdfCharacterExtractionResult"/> containing extracted fields,
    /// a best-effort mapped character, unmapped fields, and any warnings.
    /// </returns>
    Task<PdfCharacterExtractionResult> ExtractAsync(Stream pdfStream, CancellationToken ct = default);
}
