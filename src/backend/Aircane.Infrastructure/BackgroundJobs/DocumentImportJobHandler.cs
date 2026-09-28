using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// Handles <see cref="DocumentImportJob"/> by delegating to the existing
/// <see cref="IDocumentImportJob"/> import pipeline (text extraction, chunking, OCR detection,
/// embedding, indexing).
/// </summary>
public sealed class DocumentImportJobHandler : IJobHandler<DocumentImportJobMessage>
{
    private readonly IDocumentImportJob _importJob;
    private readonly ILogger<DocumentImportJobHandler> _logger;

    public DocumentImportJobHandler(
        IDocumentImportJob importJob,
        ILogger<DocumentImportJobHandler> logger)
    {
        _importJob = importJob;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(DocumentImportJobMessage job, CancellationToken ct)
    {
        _logger.LogDebug(
            "DocumentImportJobHandler processing document {DocumentId} (job {JobId}).",
            job.TargetDocumentId, job.JobId);

        await _importJob.ProcessDocumentAsync(job.TargetDocumentId, job.JobId, ct);
    }
}
