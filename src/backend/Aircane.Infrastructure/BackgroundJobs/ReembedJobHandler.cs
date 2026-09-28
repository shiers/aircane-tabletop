using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// Handles <see cref="ReembedJob"/> by regenerating embeddings for a single document or the
/// entire library via <see cref="ILibraryService"/>.
/// </summary>
public sealed class ReembedJobHandler : IJobHandler<ReembedJobMessage>
{
    private readonly ILibraryService _libraryService;
    private readonly ILogger<ReembedJobHandler> _logger;

    public ReembedJobHandler(
        ILibraryService libraryService,
        ILogger<ReembedJobHandler> logger)
    {
        _libraryService = libraryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ReembedJobMessage job, CancellationToken ct)
    {
        if (job.TargetDocumentId is Guid documentId)
        {
            var count = await _libraryService.ReEmbedDocumentAsync(documentId, ct);
            _logger.LogInformation(
                "Re-embed job {JobId} re-embedded {Count} chunk(s) for document {DocumentId}.",
                job.JobId, count, documentId);
        }
        else
        {
            var count = await _libraryService.ReEmbedAllAsync(ct);
            _logger.LogInformation(
                "Re-embed-all job {JobId} re-embedded {Count} chunk(s) across the library.",
                job.JobId, count);
        }
    }
}
