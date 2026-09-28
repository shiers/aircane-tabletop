using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// Handles <see cref="ReocrJobMessage"/> by re-running the import pipeline on documents currently
/// marked <see cref="ImportStatus.OcrRequired"/>. Used after a host enables OCR (or full-page
/// rasterization) to process previously-skipped scanned documents without re-importing them.
/// </summary>
public sealed class ReocrJobHandler : IJobHandler<ReocrJobMessage>
{
    private readonly AircaneDbContext _db;
    private readonly IDocumentImportService _importService;
    private readonly ILogger<ReocrJobHandler> _logger;

    public ReocrJobHandler(
        AircaneDbContext db,
        IDocumentImportService importService,
        ILogger<ReocrJobHandler> logger)
    {
        _db = db;
        _importService = importService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ReocrJobMessage job, CancellationToken ct)
    {
        var query = _db.SourceDocuments.Where(d => d.ImportStatus == ImportStatus.OcrRequired);
        if (job.TargetDocumentId is Guid id)
            query = query.Where(d => d.Id == id);

        var documentIds = await query.Select(d => d.Id).ToListAsync(ct);

        foreach (var documentId in documentIds)
        {
            ct.ThrowIfCancellationRequested();

            // Reset to Pending and enqueue a fresh import job, which will retry OCR now that it's
            // available.
            var document = await _db.SourceDocuments.FirstAsync(d => d.Id == documentId, ct);
            document.ImportStatus = ImportStatus.Pending;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _importService.EnqueueImportJobAsync(documentId, ct);
        }

        _logger.LogInformation(
            "Re-OCR job {JobId} re-enqueued {Count} OCR-required document(s).",
            job.JobId, documentIds.Count);
    }
}
