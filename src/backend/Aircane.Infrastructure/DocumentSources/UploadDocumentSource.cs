using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.DocumentSources;

/// <summary>
/// Stub <see cref="IDocumentSource"/> implementation reserved for future cloud/upload mode.
/// All methods throw <see cref="NotImplementedException"/> until the upload pipeline is built.
/// </summary>
/// <remarks>
/// This class exists to reserve the seam in the DI container and the domain model.
/// When cloud/upload mode is implemented, replace the body of each method with real
/// storage-provider logic (e.g. Amazon S3, Azure Blob Storage, or a server-side upload directory).
/// </remarks>
public sealed class UploadDocumentSource : IDocumentSource
{
    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown — upload mode is not yet implemented.</exception>
    public Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(
        string folderPath,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "UploadDocumentSource is a stub reserved for future cloud/upload mode. " +
            "Implement this method when adding upload-based document sourcing.");

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown — upload mode is not yet implemented.</exception>
    public Task<Stream> OpenStreamAsync(
        string sourcePath,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "UploadDocumentSource is a stub reserved for future cloud/upload mode. " +
            "Implement this method when adding upload-based document sourcing.");

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown — upload mode is not yet implemented.</exception>
    public Task<bool> FileExistsAsync(
        string sourcePath,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "UploadDocumentSource is a stub reserved for future cloud/upload mode. " +
            "Implement this method when adding upload-based document sourcing.");
}
