namespace Aircane.Application.Abstractions;

/// <summary>
/// Abstracts local (and future cloud) file storage for uploaded source documents.
/// Files are stored with GUID-based names to prevent path traversal and collisions.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Saves a file stream to the configured storage location.
    /// Returns the relative storage path that should be persisted in the database.
    /// </summary>
    Task<string> SaveFileAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a previously stored file by its storage path.
    /// Does not throw if the file does not exist.
    /// </summary>
    Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default);
}
