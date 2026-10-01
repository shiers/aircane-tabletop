using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.DTOs.Library;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// Backs <see cref="IDocumentImportService"/> with the in-process background job queue.
/// Enqueuing returns immediately; the <c>BackgroundJobWorker</c> runs the import.
/// </summary>
public sealed class DocumentImportService : IDocumentImportService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly AircaneDbContext _db;
    private readonly ILogger<DocumentImportService> _logger;

    public DocumentImportService(
        IBackgroundJobQueue queue,
        AircaneDbContext db,
        ILogger<DocumentImportService> logger)
    {
        _queue = queue;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueImportJobAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var job = new DocumentImportJobMessage(documentId);
        await _queue.EnqueueAsync(job, cancellationToken);

        _logger.LogInformation(
            "Enqueued document import job {JobId} for document {DocumentId}.",
            job.JobId, documentId);

        return job.JobId;
    }

    /// <inheritdoc />
    public async Task<ImportStatusDto> GetJobStatusAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.SourceDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        return new ImportStatusDto(
            DocumentId: document.Id,
            Status: document.ImportStatus,
            ProgressPercent: null,
            ErrorMessage: null,
            UpdatedAt: document.UpdatedAt,
            IsSourceAvailable: document.IsSourceAvailable);
    }
}
