using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Exposes license and attribution metadata for built-in rules content, and the verbatim
/// OGL / Section 15 texts required to satisfy license obligations.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Returns license/attribution metadata for all built-in documents.
    /// </summary>
    Task<IReadOnlyList<LicenseInfoDto>> ListBuiltInLicensesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the verbatim OGL v1.0a license text for the given OGL document, or null if the
    /// document does not exist, is not built-in, is not an OGL document, or the text is missing.
    /// </summary>
    Task<string?> GetOglTextAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the verbatim Section 15 attribution chain for the given OGL document, or null if
    /// the document does not exist, is not built-in, is not an OGL document, or the text is missing.
    /// </summary>
    Task<string?> GetSection15Async(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
