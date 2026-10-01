namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Summary returned after a folder scan completes.
/// </summary>
/// <param name="FolderId">The ID of the scanned <see cref="Aircane.Domain.Entities.WatchedFolder"/>.</param>
/// <param name="FilesFound">Total number of files enumerated in the folder.</param>
/// <param name="NewFiles">Number of files that were not previously indexed and had import jobs enqueued.</param>
/// <param name="UpdatedFiles">Number of files that were already indexed but had changed since last import.</param>
/// <param name="SkippedFiles">Number of files that were already indexed and unchanged.</param>
/// <param name="ScannedAt">UTC timestamp when the scan completed.</param>
public sealed record FolderScanResultDto(
    Guid FolderId,
    int FilesFound,
    int NewFiles,
    int UpdatedFiles,
    int SkippedFiles,
    DateTimeOffset ScannedAt);
