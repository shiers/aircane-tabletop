using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Handles background document import jobs: text extraction, chunking,
/// OCR-needed detection, embedding generation, and indexing.
/// </summary>
public interface IDocumentImportService
{
    /// <summary>
    /// Enqueues a background import job for the specified document.
    /// </summary>
    Task EnqueueImportJobAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current status of the import job for a document.
    /// </summary>
    Task<ImportStatusDto> GetJobStatusAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
