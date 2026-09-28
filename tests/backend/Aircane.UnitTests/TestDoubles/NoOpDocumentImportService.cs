using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Enums;

namespace Aircane.UnitTests.TestDoubles;

/// <summary>
/// Shared no-op <see cref="IDocumentImportService"/> for unit tests that construct
/// <c>LibraryService</c>/<c>FolderScanJob</c> but do not exercise the background import queue.
/// Records the ids it was asked to enqueue so tests can assert on them when relevant.
/// </summary>
public sealed class NoOpDocumentImportService : IDocumentImportService
{
    public List<Guid> Enqueued { get; } = new();

    public Task<Guid> EnqueueImportJobAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Enqueued.Add(documentId);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task<ImportStatusDto> GetJobStatusAsync(Guid documentId, CancellationToken cancellationToken = default)
        => Task.FromResult(new ImportStatusDto(documentId, ImportStatus.Pending, null, null, DateTimeOffset.UtcNow));
}
