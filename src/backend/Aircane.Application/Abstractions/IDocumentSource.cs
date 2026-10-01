namespace Aircane.Application.Abstractions;

/// <summary>
/// Represents a single file entry returned by <see cref="IDocumentSource.ListFilesAsync"/>.
/// </summary>
/// <param name="SourcePath">
/// The canonical path used to open the file via <see cref="IDocumentSource.OpenStreamAsync"/>.
/// For FolderWatch mode this is the absolute filesystem path.
/// For Upload mode this is the managed storage key.
/// </param>
/// <param name="FileName">The bare filename including extension (no directory component).</param>
/// <param name="LastModifiedUtc">UTC timestamp of the last modification, if available.</param>
/// <param name="SizeBytes">File size in bytes, if available.</param>
public sealed record DocumentSourceFile(
    string SourcePath,
    string FileName,
    DateTimeOffset? LastModifiedUtc,
    long? SizeBytes);

/// <summary>
/// Abstracts how source documents are physically located and opened.
/// The import pipeline depends only on this interface so that FolderWatch (local/desktop)
/// and Upload (future cloud/web) modes are interchangeable without touching domain logic.
/// </summary>
public interface IDocumentSource
{
    /// <summary>
    /// Returns the list of importable files found at <paramref name="folderPath"/>.
    /// For FolderWatch mode this enumerates the filesystem directory.
    /// For Upload mode this may enumerate a managed storage prefix.
    /// </summary>
    /// <param name="folderPath">
    /// The folder path or storage prefix to enumerate.
    /// For FolderWatch mode this is the <see cref="Domain.Entities.WatchedFolder.AbsolutePath"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of <see cref="DocumentSourceFile"/> entries.</returns>
    Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(string folderPath, CancellationToken ct = default);

    /// <summary>
    /// Opens a readable stream for the file identified by <paramref name="sourcePath"/>.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    /// <param name="sourcePath">
    /// The <see cref="DocumentSourceFile.SourcePath"/> value returned by <see cref="ListFilesAsync"/>,
    /// or the <c>SourcePath</c> stored on a <see cref="Domain.Entities.SourceDocument"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A readable <see cref="Stream"/> positioned at the beginning of the file.</returns>
    Task<Stream> OpenStreamAsync(string sourcePath, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the file identified by <paramref name="sourcePath"/> is currently accessible.
    /// Returns <c>false</c> if the file does not exist or cannot be reached (e.g. folder unmounted).
    /// </summary>
    /// <param name="sourcePath">The source path to check.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> FileExistsAsync(string sourcePath, CancellationToken ct = default);
}
