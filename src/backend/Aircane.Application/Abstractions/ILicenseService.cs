using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Exposes license and attribution metadata for built-in rules content.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Returns license/attribution metadata for all built-in documents.
    /// </summary>
    Task<IReadOnlyList<LicenseInfoDto>> ListBuiltInLicensesAsync(
        CancellationToken cancellationToken = default);
}
