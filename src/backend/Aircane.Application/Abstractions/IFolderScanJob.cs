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

    /// <summary>
    /// Analyzes the folder and returns a <see cref="FolderScanPreviewDto"/> describing each
    /// discovered file (suggested title/ruleset, dedup grouping, and advisory flags) WITHOUT
    /// creating any records or enqueuing imports. Files already imported (by path) are still
    /// included so the host sees the full picture.
    /// </summary>
    /// <param name="folderId">ID of the <see cref="Domain.Entities.WatchedFolder"/> to analyze.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when no folder with <paramref name="folderId"/> exists.</exception>
    Task<FolderScanPreviewDto> PreviewFolderAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>
    /// Imports the host-selected files from a folder-scan preview, applying per-file classification
    /// overrides. Only items with <see cref="FolderImportSelectionItem.Import"/> set to true are
    /// imported; files already indexed at the same path are skipped (idempotent). Enqueues an import
    /// job for each newly created document.
    /// </summary>
    /// <param name="request">The host's import selection and overrides.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when no folder with the given ID exists.</exception>
    Task<FolderScanResultDto> ImportSelectionAsync(
        FolderImportSelectionRequest request,
        CancellationToken ct = default);
}
