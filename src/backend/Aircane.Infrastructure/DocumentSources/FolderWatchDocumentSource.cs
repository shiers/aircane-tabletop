using Aircane.Application.Abstractions;
using Aircane.Application.Validation;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.DocumentSources;

/// <summary>
/// <see cref="IDocumentSource"/> implementation for FolderWatch mode.
/// Reads files directly from the local filesystem using the absolute path
/// registered in a <see cref="Domain.Entities.WatchedFolder"/>.
/// Source files are never copied; the app reads them in place.
/// </summary>
public sealed class FolderWatchDocumentSource : IDocumentSource
{
    /// <summary>
    /// File extensions that the import pipeline can process.
    /// Only files with these extensions are returned by <see cref="ListFilesAsync"/>.
    /// </summary>
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".json" };

    private readonly ILogger<FolderWatchDocumentSource> _logger;

    public FolderWatchDocumentSource(ILogger<FolderWatchDocumentSource> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Enumerates the top-level files in <paramref name="folderPath"/> that have a supported
    /// extension (.pdf, .json). Subdirectories are not traversed.
    /// Files whose canonical path escapes the folder boundary are excluded.
    /// Returns an empty list if the directory does not exist rather than throwing.
    /// </remarks>
    public Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(
        string folderPath,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(folderPath))
        {
            _logger.LogWarning(
                "FolderWatchDocumentSource: directory '{FolderPath}' does not exist or is not accessible.",
                folderPath);

            return Task.FromResult<IReadOnlyList<DocumentSourceFile>>(Array.Empty<DocumentSourceFile>());
        }

        IReadOnlyList<DocumentSourceFile> files;

        try
        {
            files = Directory
                .EnumerateFiles(folderPath)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .Where(path => FolderPathValidator.IsFileWithinFolder(path, folderPath))
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new DocumentSourceFile(
                        SourcePath: path,
                        FileName: info.Name,
                        LastModifiedUtc: info.LastWriteTimeUtc,
                        SizeBytes: info.Length);
                })
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex,
                "FolderWatchDocumentSource: failed to enumerate directory '{FolderPath}'.",
                folderPath);

            files = Array.Empty<DocumentSourceFile>();
        }

        return Task.FromResult(files);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Opens the file at <paramref name="sourcePath"/> as an async-capable read-only stream.
    /// Throws <see cref="FileNotFoundException"/> if the file does not exist.
    /// Throws <see cref="UnauthorizedAccessException"/> if the file path escapes the expected folder boundary.
    /// </remarks>
    public Task<Stream> OpenStreamAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                $"Source file not found at path '{sourcePath}'.", sourcePath);
        }

        Stream stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string sourcePath, CancellationToken ct = default)
    {
        var exists = File.Exists(sourcePath);
        return Task.FromResult(exists);
    }
}
