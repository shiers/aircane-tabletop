namespace Aircane.Application.DocumentProcessing;

/// <summary>
/// Result of extracting text from a PDF document.
/// </summary>
/// <param name="Pages">Text extracted per page.</param>
/// <param name="IsOcrRequired">
/// True when the document has very sparse or no extractable text,
/// indicating that OCR would be needed to process it.
/// </param>
/// <param name="TotalCharacters">Total character count across all pages.</param>
public record PdfExtractionResult(
    IReadOnlyList<PageText> Pages,
    bool IsOcrRequired,
    int TotalCharacters);
