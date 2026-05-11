using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Validation;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Library;

/// <summary>
/// Implements <see cref="IFolderScanJob"/> by enumerating files in a registered watched folder,
/// creating or updating <see cref="Domain.Entities.SourceDocument"/> records, and enqueuing
/// import jobs for new or changed files.
/// </summary>
public sealed class FolderScanJob : IFolderScanJob
{
    private readonly IDocumentSource _documentSource;
    private readonly ILibraryService _libraryService;
    private readonly IDocumentImportJob _documentImportJob;
    private readonly AircaneDbContext _db;
    private readonly ILogger<FolderScanJob> _logger;

    public FolderScanJob(
        IDocumentSource documentSource,
        ILibraryService libraryService,
        IDocumentImportJob documentImportJob,
        AircaneDbContext db,
        ILogger<FolderScanJob> logger)
    {
        _documentSource = documentSource;
        _libraryService = libraryService;
        _documentImportJob = documentImportJob;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FolderScanResultDto> ScanFolderAsync(Guid folderId, CancellationToken ct = default)
    {
        // ── 1. Load the watched folder ────────────────────────────────────────
        var folder = await _db.WatchedFolders
            .FirstOrDefaultAsync(f => f.Id == folderId, ct)
            ?? throw new KeyNotFoundException($"Watched folder {folderId} not found.");

        _logger.LogInformation(
            "Starting folder scan. FolderId={FolderId}, Path={Path}",
            folderId, folder.AbsolutePath);

        // ── 2. Enumerate files in the folder ──────────────────────────────────
        var files = await _documentSource.ListFilesAsync(folder.AbsolutePath, ct);

        int newFiles = 0;
        int updatedFiles = 0;
        int skippedFiles = 0;

        // ── 3. Load existing SourceDocuments for this folder (keyed by SourcePath) ──
        var existingDocuments = await _db.SourceDocuments
            .Where(d => d.WatchedFolderId == folderId)
            .ToListAsync(ct);

        var existingByPath = existingDocuments
            .ToDictionary(d => d.SourcePath, StringComparer.OrdinalIgnoreCase);

        // ── 4. Process each discovered file ───────────────────────────────────
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            // ── Safety check: verify the file is actually within the registered folder ──
            if (!FolderPathValidator.IsFileWithinFolder(file.SourcePath, folder.AbsolutePath))
            {
                _logger.LogWarning(
                    "File path escapes registered folder boundary. Skipping. " +
                    "SourcePath={SourcePath}, FolderPath={FolderPath}",
                    file.SourcePath, folder.AbsolutePath);
                skippedFiles++;
                continue;
            }

            // ── Sanitize the filename for use in SourceDocument records ──
            var sanitizedFileName = FolderPathValidator.SanitizeFileName(file.FileName);

            if (existingByPath.TryGetValue(file.SourcePath, out var existingDoc))
            {
                // File already indexed — check whether it has changed.
                bool hasChanged = file.LastModifiedUtc.HasValue
                    && file.LastModifiedUtc.Value > existingDoc.UpdatedAt;

                if (!hasChanged)
                {
                    _logger.LogDebug(
                        "File unchanged, skipping. SourcePath={SourcePath}", file.SourcePath);
                    skippedFiles++;
                    continue;
                }

                // File changed — reset status and re-import.
                _logger.LogInformation(
                    "File changed, re-importing. DocumentId={DocumentId}, SourcePath={SourcePath}",
                    existingDoc.Id, file.SourcePath);

                existingDoc.ImportStatus = ImportStatus.Pending;
                existingDoc.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                await _documentImportJob.ProcessDocumentAsync(existingDoc.Id, ct);
                updatedFiles++;
            }
            else
            {
                // New file — create a SourceDocument record and import it.
                _logger.LogInformation(
                    "New file discovered, creating document. SourcePath={SourcePath}", file.SourcePath);

                var newDoc = await _libraryService.CreateDocumentFromFolderAsync(
                    folderId,
                    file.SourcePath,
                    sanitizedFileName,
                    ct);

                await _documentImportJob.ProcessDocumentAsync(newDoc.Id, ct);
                newFiles++;
            }
        }

        // ── 5. Mark documents whose source paths are no longer in the folder ─────
        var discoveredPaths = new HashSet<string>(
            files.Select(f => f.SourcePath),
            StringComparer.OrdinalIgnoreCase);

        int unavailableFiles = 0;
        foreach (var doc in existingDocuments)
        {
            if (!discoveredPaths.Contains(doc.SourcePath) && doc.IsSourceAvailable)
            {
                _logger.LogWarning(
                    "Source file no longer found in folder. DocumentId={DocumentId}, SourcePath={SourcePath}. " +
                    "Marking as unavailable.",
                    doc.Id, doc.SourcePath);

                doc.IsSourceAvailable = false;
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                unavailableFiles++;
            }
        }

        if (unavailableFiles > 0)
            await _db.SaveChangesAsync(ct);

        // ── 6. Update LastScannedAt ───────────────────────────────────────────
        var scannedAt = DateTimeOffset.UtcNow;
        folder.LastScannedAt = scannedAt;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Folder scan complete. FolderId={FolderId}, FilesFound={FilesFound}, " +
            "New={New}, Updated={Updated}, Skipped={Skipped}, Unavailable={Unavailable}",
            folderId, files.Count, newFiles, updatedFiles, skippedFiles, unavailableFiles);

        return new FolderScanResultDto(
            FolderId: folderId,
            FilesFound: files.Count,
            NewFiles: newFiles,
            UpdatedFiles: updatedFiles,
            SkippedFiles: skippedFiles,
            ScannedAt: scannedAt);
    }
}
