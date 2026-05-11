namespace Aircane.Application.Abstractions;

/// <summary>
/// Processes a single document import job: extracts text, creates chunks,
/// and updates the document's <see cref="Domain.Enums.ImportStatus"/>.
/// </summary>
public interface IDocumentImportJob
{
    /// <summary>
    /// Processes the import for the specified document.
    /// Sets the document status to <c>Processing</c>, extracts content,
    /// persists chunks, and sets the final status to <c>Completed</c>,
    /// <c>OcrRequired</c>, or <c>Failed</c>.
    /// </summary>
    /// <param name="documentId">The ID of the <see cref="Domain.Entities.SourceDocument"/> to process.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ProcessDocumentAsync(Guid documentId, CancellationToken ct = default);
}
