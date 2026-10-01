using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Library;
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
    private readonly IDocumentImportService _documentImportService;
    private readonly IScanCandidateAnalyzer _analyzer;
    private readonly AircaneDbContext _db;
    private readonly ILogger<FolderScanJob> _logger;

    public FolderScanJob(
        IDocumentSource documentSource,
        ILibraryService libraryService,
        IDocumentImportService documentImportService,
        IScanCandidateAnalyzer analyzer,
        AircaneDbContext db,
        ILogger<FolderScanJob> logger)
    {
        _documentSource = documentSource;
        _libraryService = libraryService;
        _documentImportService = documentImportService;
        _analyzer = analyzer;
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
                // File already indexed - check whether it has changed.
                bool hasChanged = file.LastModifiedUtc.HasValue
                    && file.LastModifiedUtc.Value > existingDoc.UpdatedAt;

                if (!hasChanged)
                {
                    _logger.LogDebug(
                        "File unchanged, skipping. SourcePath={SourcePath}", file.SourcePath);
                    skippedFiles++;
                    continue;
                }

                // File changed - reset status and re-import.
                _logger.LogInformation(
                    "File changed, re-importing. DocumentId={DocumentId}, SourcePath={SourcePath}",
                    existingDoc.Id, file.SourcePath);

                existingDoc.ImportStatus = ImportStatus.Pending;
                existingDoc.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                await _documentImportService.EnqueueImportJobAsync(existingDoc.Id, ct);
                updatedFiles++;
            }
            else
            {
                // New file - create a SourceDocument record and import it.
                _logger.LogInformation(
                    "New file discovered, creating document. SourcePath={SourcePath}", file.SourcePath);

                var newDoc = await _libraryService.CreateDocumentFromFolderAsync(
                    folderId,
                    file.SourcePath,
                    sanitizedFileName,
                    overrides: null,
                    ct);

                await _documentImportService.EnqueueImportJobAsync(newDoc.Id, ct);
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

    /// <inheritdoc />
    public async Task<FolderScanPreviewDto> PreviewFolderAsync(Guid folderId, CancellationToken ct = default)
    {
        var folder = await _db.WatchedFolders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId, ct)
            ?? throw new KeyNotFoundException($"Watched folder {folderId} not found.");

        _logger.LogInformation(
            "Previewing folder scan. FolderId={FolderId}, Path={Path}", folderId, folder.AbsolutePath);

        // Enumerate discoverable files (supported extensions, within-boundary) — read only.
        var files = await _documentSource.ListFilesAsync(folder.AbsolutePath, ct);

        // Compute the set of dedup keys already represented in the library so the analyzer can flag
        // re-imports. Derived from every existing document's original filename.
        var existingFileNames = await _db.SourceDocuments
            .AsNoTracking()
            .Select(d => d.OriginalFileName)
            .ToListAsync(ct);

        var existingDedupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in existingFileNames)
        {
            var key = FilenameNormalizer.ComputeDedupKey(name);
            if (!string.IsNullOrEmpty(key))
                existingDedupKeys.Add(key);
        }

        // Content-based duplicate detection: hash each discovered file and compare against the
        // content hashes already indexed in the library (cross-folder exact-duplicate detection).
        var existingContentHashes = new HashSet<string>(
            (await _db.SourceDocuments
                .AsNoTracking()
                .Where(d => d.ContentHash != null)
                .Select(d => d.ContentHash!)
                .ToListAsync(ct)),
            StringComparer.OrdinalIgnoreCase);

        var fileContentHashes = await ComputeContentHashesAsync(files, ct);

        var candidates = _analyzer.Analyze(
            files,
            existingDedupKeys,
            fileContentHashes,
            existingContentHashes,
            folder.ExcludePatterns);

        _logger.LogInformation(
            "Folder preview complete. FolderId={FolderId}, FilesFound={FilesFound}", folderId, files.Count);

        return new FolderScanPreviewDto(
            FolderId: folderId,
            FilesFound: files.Count,
            Candidates: candidates,
            AnalyzedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Computes SHA-256 content hashes (lowercase hex) for the discovered files, keyed by source
    /// path. Files that cannot be opened are skipped (no hash entry). Best-effort so a single
    /// unreadable file does not fail the whole preview.
    /// </summary>
    private async Task<Dictionary<string, string>> ComputeContentHashesAsync(
        IReadOnlyList<DocumentSourceFile> files,
        CancellationToken ct)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = await _documentSource.OpenStreamAsync(file.SourcePath, ct);
                using var sha = System.Security.Cryptography.SHA256.Create();
                var bytes = await sha.ComputeHashAsync(stream, ct);
                hashes[file.SourcePath] = Convert.ToHexString(bytes).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Could not hash file during preview; skipping content-duplicate check for {SourcePath}.",
                    file.SourcePath);
            }
        }
        return hashes;
    }

    /// <inheritdoc />
    public async Task<FolderScanResultDto> ImportSelectionAsync(
        FolderImportSelectionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var folder = await _db.WatchedFolders
            .FirstOrDefaultAsync(f => f.Id == request.FolderId, ct)
            ?? throw new KeyNotFoundException($"Watched folder {request.FolderId} not found.");

        _logger.LogInformation(
            "Importing folder selection. FolderId={FolderId}, ItemCount={ItemCount}",
            request.FolderId, request.Items.Count);

        // Re-enumerate the folder so we only act on files that currently exist and are within the
        // folder boundary — the preview the host acted on may be slightly stale.
        var discovered = await _documentSource.ListFilesAsync(folder.AbsolutePath, ct);
        var discoveredByPath = discovered.ToDictionary(f => f.SourcePath, StringComparer.OrdinalIgnoreCase);

        // Existing documents for this folder, keyed by path, to preserve idempotency.
        var existingByPath = (await _db.SourceDocuments
                .Where(d => d.WatchedFolderId == request.FolderId)
                .ToListAsync(ct))
            .ToDictionary(d => d.SourcePath, StringComparer.OrdinalIgnoreCase);

        int newFiles = 0;
        int skippedFiles = 0;

        foreach (var item in request.Items)
        {
            ct.ThrowIfCancellationRequested();

            if (!item.Import)
            {
                skippedFiles++;
                continue;
            }

            // Ignore selections for files that no longer exist or escaped the folder boundary.
            if (!discoveredByPath.TryGetValue(item.SourcePath, out var file) ||
                !FolderPathValidator.IsFileWithinFolder(item.SourcePath, folder.AbsolutePath))
            {
                _logger.LogWarning(
                    "Selected file is no longer available or outside the folder; skipping. SourcePath={SourcePath}",
                    item.SourcePath);
                skippedFiles++;
                continue;
            }

            // Idempotency: never create a second record for an already-indexed path.
            if (existingByPath.ContainsKey(item.SourcePath))
            {
                _logger.LogDebug(
                    "Selected file already indexed; skipping. SourcePath={SourcePath}", item.SourcePath);
                skippedFiles++;
                continue;
            }

            var overrides = new FolderDocumentOverrides(
                Title: item.Title,
                SourceType: item.SourceType,
                GameSystem: item.GameSystem,
                Ruleset: item.Ruleset);

            var newDoc = await _libraryService.CreateDocumentFromFolderAsync(
                request.FolderId, file.SourcePath, file.FileName, overrides, ct);

            await _documentImportService.EnqueueImportJobAsync(newDoc.Id, ct);
            newFiles++;
        }

        var scannedAt = DateTimeOffset.UtcNow;
        folder.LastScannedAt = scannedAt;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Folder selection import complete. FolderId={FolderId}, New={New}, Skipped={Skipped}",
            request.FolderId, newFiles, skippedFiles);

        return new FolderScanResultDto(
            FolderId: request.FolderId,
            FilesFound: discovered.Count,
            NewFiles: newFiles,
            UpdatedFiles: 0,
            SkippedFiles: skippedFiles,
            ScannedAt: scannedAt);
    }
}
