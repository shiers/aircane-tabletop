using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Manages watched folder registration, source document metadata, upload validation,
/// source classification, visibility policy, and library search.
/// </summary>
public interface ILibraryService
{
    // ── Watched Folder CRUD ───────────────────────────────────────────────────

    /// <summary>
    /// Registers a new watched folder and persists it to the database.
    /// </summary>
    Task<WatchedFolderDto> RegisterFolderAsync(
        RegisterFolderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all registered watched folders.
    /// </summary>
    Task<IReadOnlyList<WatchedFolderDto>> ListFoldersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single watched folder by ID, or null if not found.
    /// </summary>
    Task<WatchedFolderDto?> GetFolderAsync(
        Guid folderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the settings of an existing watched folder.
    /// Only non-null fields in the request are applied.
    /// </summary>
    Task<WatchedFolderDto> UpdateFolderAsync(
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a watched folder record and all associated SourceDocument records and their
    /// DocumentChunks. Does not touch source files on disk.
    /// </summary>
    Task DeleteFolderAsync(
        Guid folderId,
        CancellationToken cancellationToken = default);

    // ── Source Document CRUD ──────────────────────────────────────────────────
    /// <summary>
    /// Uploads a document, stores the file, creates the metadata record,
    /// and enqueues an import job.
    /// </summary>
    Task<SourceDocumentDto> UploadDocumentAsync(
        UploadDocumentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged list of source documents matching the optional filters.
    /// </summary>
    Task<IReadOnlyList<SourceDocumentDto>> ListDocumentsAsync(
        DocumentListRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for a single document. Does not return the file content.
    /// </summary>
    Task<SourceDocumentDto?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current import job status and progress for a document.
    /// </summary>
    Task<ImportStatusDto> GetImportStatusAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enqueues the import job for a document, re-running extraction,
    /// chunking, and embedding generation.
    /// </summary>
    Task ReindexDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document record, its stored file, and all indexed chunks.
    /// </summary>
    Task DeleteDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the classification metadata (title, source type, game system, ruleset, tags)
    /// of an existing document. Only non-null fields in the request are applied.
    /// </summary>
    Task<SourceDocumentDto> UpdateClassificationAsync(
        Guid documentId,
        UpdateClassificationRequest request,
        CancellationToken cancellationToken = default);

    // ── Folder-scan document creation ─────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="SourceDocumentDto"/> for a file discovered during a folder scan,
    /// inheriting <c>DefaultSourceType</c>, <c>DefaultGameSystem</c>, and <c>DefaultRuleset</c>
    /// from the parent <see cref="Aircane.Domain.Entities.WatchedFolder"/>.
    /// Sets <c>SourceMode = FolderWatch</c>, <c>WatchedFolderId = folderId</c>, and
    /// <c>IsSourceAvailable = true</c>.
    /// </summary>
    /// <param name="folderId">ID of the parent <see cref="Aircane.Domain.Entities.WatchedFolder"/>.</param>
    /// <param name="sourcePath">Absolute filesystem path to the discovered file.</param>
    /// <param name="originalFileName">Original filename (used for the document title and metadata).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted <see cref="SourceDocumentDto"/> with inherited classification defaults.</returns>
    Task<SourceDocumentDto> CreateDocumentFromFolderAsync(
        Guid folderId,
        string sourcePath,
        string originalFileName,
        CancellationToken cancellationToken = default);
}
