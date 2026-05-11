using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Scans a registered watched folder, creates or updates <see cref="Domain.Entities.SourceDocument"/>
/// records for new or changed files, and enqueues import jobs.
/// </summary>
public interface IFolderScanJob
{
    /// <summary>
    /// Scans the folder identified by <paramref name="folderId"/>.
    /// <list type="bullet">
    ///   <item>New files: creates a <see cref="Domain.Entities.SourceDocument"/> and enqueues an import job.</item>
    ///   <item>Changed files: resets <c>ImportStatus</c> to <c>Pending</c> and enqueues an import job.</item>
    ///   <item>Unchanged files: skipped.</item>
    /// </list>
    /// Updates <see cref="Domain.Entities.WatchedFolder.LastScannedAt"/> after the scan completes.
    /// </summary>
    /// <param name="folderId">ID of the <see cref="Domain.Entities.WatchedFolder"/> to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="FolderScanResultDto"/> summarising the scan outcome.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no folder with <paramref name="folderId"/> exists.</exception>
    Task<FolderScanResultDto> ScanFolderAsync(Guid folderId, CancellationToken ct = default);
}
