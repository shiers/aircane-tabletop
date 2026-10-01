using Aircane.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Storage;

/// <summary>
/// Stores uploaded source documents on the local filesystem.
/// Files are saved with GUID-based names under the configured documents path.
/// The configured path is never exposed to callers; only relative paths are returned.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _documentsPath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IConfiguration configuration, ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;

        var configuredPath = configuration["Storage:DocumentsPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "./data/documents";
        }

        // Resolve to an absolute path so we never depend on the working directory at runtime.
        _documentsPath = Path.GetFullPath(configuredPath);
    }

    /// <inheritdoc />
    public async Task<string> SaveFileAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        // Normalise extension: ensure it starts with a dot and is lowercase.
        if (!extension.StartsWith('.'))
            extension = "." + extension;
        extension = extension.ToLowerInvariant();

        Directory.CreateDirectory(_documentsPath);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(_documentsPath, fileName);

        _logger.LogInformation("Saving uploaded document to {FullPath}", fullPath);

        await using var fileStream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(fileStream, cancellationToken);

        // Return a relative path so the stored value is portable.
        return fileName;
    }

    /// <inheritdoc />
    public Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        // storagePath is the filename (relative) returned by SaveFileAsync.
        var fullPath = Path.Combine(_documentsPath, storagePath);

        if (File.Exists(fullPath))
        {
            _logger.LogInformation("Deleting stored document at {FullPath}", fullPath);
            File.Delete(fullPath);
        }
        else
        {
            _logger.LogWarning("Attempted to delete non-existent file at {FullPath}", fullPath);
        }

        return Task.CompletedTask;
    }
}
