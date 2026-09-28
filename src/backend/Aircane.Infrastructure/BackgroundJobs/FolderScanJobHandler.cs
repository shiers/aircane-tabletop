using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// Handles <see cref="FolderScanJob"/> by delegating to the existing folder-scan pipeline.
/// </summary>
public sealed class FolderScanJobHandler : IJobHandler<FolderScanJobMessage>
{
    private readonly IFolderScanJob _folderScanJob;
    private readonly ILogger<FolderScanJobHandler> _logger;

    public FolderScanJobHandler(
        IFolderScanJob folderScanJob,
        ILogger<FolderScanJobHandler> logger)
    {
        _folderScanJob = folderScanJob;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(FolderScanJobMessage job, CancellationToken ct)
    {
        _logger.LogDebug(
            "FolderScanJobHandler scanning folder {FolderId} (job {JobId}).",
            job.FolderId, job.JobId);

        var result = await _folderScanJob.ScanFolderAsync(job.FolderId, ct);

        _logger.LogInformation(
            "Folder scan {JobId} complete for folder {FolderId}. New={New}, Updated={Updated}, Skipped={Skipped}.",
            job.JobId, job.FolderId, result.NewFiles, result.UpdatedFiles, result.SkippedFiles);
    }
}
