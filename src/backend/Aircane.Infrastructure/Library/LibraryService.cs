using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Library;

/// <summary>
/// Implements <see cref="ILibraryService"/> using EF Core and the local filesystem.
/// Handles watched folder registration, upload validation, file storage, and SourceDocument record management.
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private readonly AircaneDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(
        AircaneDbContext db,
        IFileStorageService fileStorage,
        ILogger<LibraryService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    // ── Watched Folder CRUD ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<WatchedFolderDto> RegisterFolderAsync(
        RegisterFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        var folder = new WatchedFolder(
            displayName: request.DisplayName,
            absolutePath: request.AbsolutePath,
            defaultSourceType: request.DefaultSourceType,
            defaultGameSystem: request.DefaultGameSystem,
            defaultRuleset: request.DefaultRuleset);

        _db.WatchedFolders.Add(folder);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Watched folder '{DisplayName}' registered. Id={Id}, Path={Path}",
            folder.DisplayName, folder.Id, folder.AbsolutePath);

        return MapFolderToDto(folder);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WatchedFolderDto>> ListFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var folders = await _db.WatchedFolders
            .AsNoTracking()
            .OrderBy(f => f.DisplayName)
            .ToListAsync(cancellationToken);

        return folders.Select(MapFolderToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<WatchedFolderDto?> GetFolderAsync(
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var folder = await _db.WatchedFolders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken);

        return folder is null ? null : MapFolderToDto(folder);
    }

    /// <inheritdoc />
    public async Task<WatchedFolderDto> UpdateFolderAsync(
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        var folder = await _db.WatchedFolders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Watched folder {folderId} not found.");

        if (request.DisplayName is not null)
            folder.DisplayName = request.DisplayName;

        if (request.AbsolutePath is not null)
            folder.AbsolutePath = request.AbsolutePath;

        if (request.DefaultSourceType.HasValue)
            folder.DefaultSourceType = request.DefaultSourceType.Value;

        // Allow explicit null to clear optional fields
        if (request.DefaultGameSystem is not null)
            folder.DefaultGameSystem = request.DefaultGameSystem;

        if (request.DefaultRuleset is not null)
            folder.DefaultRuleset = request.DefaultRuleset;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Watched folder {FolderId} updated.", folderId);

        return MapFolderToDto(folder);
    }

    /// <inheritdoc />
    public async Task DeleteFolderAsync(
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var folder = await _db.WatchedFolders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Watched folder {folderId} not found.");

        // Load all source documents belonging to this folder so we can cascade-delete their chunks.
        // We do this explicitly because the FK on SourceDocument uses SetNull (not Cascade) to
        // support the case where a document outlives its folder (e.g. Upload mode). For folder
        // deletion we want a hard cascade: folder → documents → chunks.
        var documentIds = await _db.SourceDocuments
            .Where(d => d.WatchedFolderId == folderId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        if (documentIds.Count > 0)
        {
            // Delete all chunks for these documents in bulk.
            await _db.DocumentChunks
                .Where(c => documentIds.Contains(c.SourceDocumentId))
                .ExecuteDeleteAsync(cancellationToken);

            // Delete the source document records.
            await _db.SourceDocuments
                .Where(d => d.WatchedFolderId == folderId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        _db.WatchedFolders.Remove(folder);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Watched folder {FolderId} deleted along with {DocumentCount} associated document(s).",
            folderId, documentIds.Count);
    }

    /// <inheritdoc />
    public async Task<SourceDocumentDto> UploadDocumentAsync(
        UploadDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Validate file extension ────────────────────────────────────────
        if (!FileValidationHelper.IsExtensionAllowed(request.OriginalFileName))
        {
            throw new InvalidOperationException(
                $"File type not allowed. Only PDF and JSON files are accepted. " +
                $"Received: '{Path.GetExtension(request.OriginalFileName)}'");
        }

        // ── 2. Validate MIME type ─────────────────────────────────────────────
        if (!string.IsNullOrEmpty(request.ContentType) &&
            !FileValidationHelper.IsMimeTypeAllowed(request.OriginalFileName, request.ContentType))
        {
            throw new InvalidOperationException(
                $"Content type '{request.ContentType}' is not allowed for " +
                $"'{Path.GetExtension(request.OriginalFileName)}' files.");
        }

        // ── 3. Validate file size ─────────────────────────────────────────────
        if (request.FileContent.Length > FileValidationHelper.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size {request.FileContent.Length:N0} bytes exceeds the maximum " +
                $"allowed size of {FileValidationHelper.MaxFileSizeBytes:N0} bytes (50 MB).");
        }

        // ── 4. Sanitize the original filename (for metadata only) ─────────────
        var sanitizedOriginalName = FileValidationHelper.SanitizeFileName(request.OriginalFileName);
        var extension = FileValidationHelper.GetNormalizedExtension(request.OriginalFileName);

        // ── 5. Save file to storage ───────────────────────────────────────────
        string storagePath;
        try
        {
            storagePath = await _fileStorage.SaveFileAsync(
                request.FileContent,
                extension,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save uploaded file '{FileName}'", sanitizedOriginalName);
            throw new InvalidOperationException("Failed to save the uploaded file. Please try again.", ex);
        }

        // ── 6. Create SourceDocument record ───────────────────────────────────
        var document = new SourceDocument(
            title: request.Title,
            originalFileName: sanitizedOriginalName,
            sourceType: request.SourceType,
            sourceMode: SourceMode.Upload,
            gameSystem: request.GameSystem,
            ruleset: request.Ruleset,
            sourcePath: storagePath,
            watchedFolderId: null,
            visibility: request.Visibility,
            importStatus: ImportStatus.Pending);

        _db.SourceDocuments.Add(document);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist SourceDocument record for '{FileName}'", sanitizedOriginalName);

            // Best-effort cleanup: remove the file we just saved.
            try { await _fileStorage.DeleteFileAsync(storagePath, CancellationToken.None); }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up file '{StoragePath}' after DB error", storagePath);
            }

            throw;
        }

        _logger.LogInformation(
            "Document '{Title}' uploaded successfully. Id={Id}, StoragePath={StoragePath}",
            document.Title, document.Id, storagePath);

        return MapToDto(document);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceDocumentDto>> ListDocumentsAsync(
        DocumentListRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _db.SourceDocuments.AsNoTracking();

        if (request.SourceType.HasValue)
            query = query.Where(d => d.SourceType == request.SourceType.Value);

        if (!string.IsNullOrWhiteSpace(request.GameSystem))
            query = query.Where(d => d.GameSystem == request.GameSystem);

        if (!string.IsNullOrWhiteSpace(request.Ruleset))
            query = query.Where(d => d.Ruleset == request.Ruleset);

        if (request.ImportStatus.HasValue)
            query = query.Where(d => d.ImportStatus == request.ImportStatus.Value);

        if (request.IsSourceAvailable.HasValue)
            query = query.Where(d => d.IsSourceAvailable == request.IsSourceAvailable.Value);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return documents.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SourceDocumentDto?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.SourceDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        return document is null ? null : MapToDto(document);
    }

    /// <inheritdoc />
    public async Task<ImportStatusDto> GetImportStatusAsync(
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

    /// <inheritdoc />
    public async Task ReindexDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.SourceDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        document.ImportStatus = ImportStatus.Pending;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Document {DocumentId} queued for reindex.", documentId);
        // Background job enqueue will be wired in task 2.3+.
    }

    /// <inheritdoc />
    public async Task DeleteDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.SourceDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        var storagePath = document.SourcePath;

        _db.SourceDocuments.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);

        // Best-effort file cleanup.
        try
        {
            await _fileStorage.DeleteFileAsync(storagePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file '{StoragePath}' for document {DocumentId}", storagePath, documentId);
        }

        _logger.LogInformation("Document {DocumentId} deleted.", documentId);
    }

    /// <inheritdoc />
    public async Task<SourceDocumentDto> UpdateClassificationAsync(
        Guid documentId,
        UpdateClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.SourceDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        if (request.Title is not null)
            document.Title = request.Title;

        if (request.SourceType.HasValue)
            document.SourceType = request.SourceType.Value;

        if (request.GameSystem is not null)
            document.GameSystem = request.GameSystem;

        if (request.Ruleset is not null)
            document.Ruleset = request.Ruleset;

        if (request.Tags is not null)
            document.Tags = [.. request.Tags];

        document.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Document {DocumentId} classification updated. SourceType={SourceType}, Ruleset={Ruleset}",
            documentId, document.SourceType, document.Ruleset);

        return MapToDto(document);
    }

    // ── Folder-scan document creation ─────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SourceDocumentDto> CreateDocumentFromFolderAsync(
        Guid folderId,
        string sourcePath,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var folder = await _db.WatchedFolders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Watched folder {folderId} not found.");

        var sanitizedFileName = FileValidationHelper.SanitizeFileName(originalFileName);

        // Derive a human-readable title from the filename (strip extension).
        var title = Path.GetFileNameWithoutExtension(sanitizedFileName);

        var document = new SourceDocument(
            title: title,
            originalFileName: sanitizedFileName,
            sourceType: folder.DefaultSourceType,
            sourceMode: SourceMode.FolderWatch,
            gameSystem: folder.DefaultGameSystem ?? string.Empty,
            ruleset: folder.DefaultRuleset ?? string.Empty,
            sourcePath: sourcePath,
            watchedFolderId: folderId,
            visibility: ContentVisibility.DMOnly,
            importStatus: ImportStatus.Pending);

        _db.SourceDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SourceDocument created from folder scan. Id={Id}, FolderId={FolderId}, Path={SourcePath}, " +
            "SourceType={SourceType}, GameSystem={GameSystem}, Ruleset={Ruleset}",
            document.Id, folderId, sourcePath,
            document.SourceType, document.GameSystem, document.Ruleset);

        return MapToDto(document);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static WatchedFolderDto MapFolderToDto(WatchedFolder folder) =>
        new(
            Id: folder.Id,
            DisplayName: folder.DisplayName,
            AbsolutePath: folder.AbsolutePath,
            DefaultSourceType: folder.DefaultSourceType,
            DefaultGameSystem: folder.DefaultGameSystem,
            DefaultRuleset: folder.DefaultRuleset,
            LastScannedAt: folder.LastScannedAt,
            CreatedAt: folder.CreatedAt);

    private static SourceDocumentDto MapToDto(SourceDocument doc) =>
        new(
            Id: doc.Id,
            Title: doc.Title,
            OriginalFileName: doc.OriginalFileName,
            SourceType: doc.SourceType,
            SourceMode: doc.SourceMode,
            GameSystem: doc.GameSystem,
            Ruleset: doc.Ruleset,
            Visibility: doc.Visibility,
            ImportStatus: doc.ImportStatus,
            IsSourceAvailable: doc.IsSourceAvailable,
            WatchedFolderId: doc.WatchedFolderId,
            CreatedAt: doc.CreatedAt,
            UpdatedAt: doc.UpdatedAt,
            Tags: doc.Tags.AsReadOnly());
}
