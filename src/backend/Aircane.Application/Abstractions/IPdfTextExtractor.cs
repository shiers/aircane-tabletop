using Aircane.Application.DocumentProcessing;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Extracts text content from a PDF document stream.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts text from each page of the provided PDF stream.
    /// </summary>
    /// <param name="pdfStream">A readable stream containing the PDF file content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PdfExtractionResult"/> containing per-page text and an OCR-required flag
    /// when the document has insufficient extractable text.
    /// </returns>
    Task<PdfExtractionResult> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default);
}
