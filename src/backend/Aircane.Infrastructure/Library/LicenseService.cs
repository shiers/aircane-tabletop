using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.License;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Library;

/// <summary>
/// Implements <see cref="ILicenseService"/> over EF Core and the embedded built-in content bundles.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private readonly AircaneDbContext _db;
    private readonly IBuiltInLicenseTextReader _licenseTextReader;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(
        AircaneDbContext db,
        IBuiltInLicenseTextReader licenseTextReader,
        ILogger<LicenseService> logger)
    {
        _db = db;
        _licenseTextReader = licenseTextReader;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenseInfoDto>> ListBuiltInLicensesAsync(
        CancellationToken cancellationToken = default)
    {
        var documents = await _db.SourceDocuments
            .AsNoTracking()
            .Where(d => d.IsBuiltIn)
            .OrderBy(d => d.GameSystem)
            .ToListAsync(cancellationToken);

        return documents.Select(MapToLicenseInfo).ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetOglTextAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var doc = await GetOglDocumentAsync(documentId, cancellationToken);
        if (doc is null)
            return null;

        return await _licenseTextReader.ReadOglTextAsync(doc.SourcePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetSection15Async(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var doc = await GetOglDocumentAsync(documentId, cancellationToken);
        if (doc is null)
            return null;

        return await _licenseTextReader.ReadSection15Async(doc.SourcePath, cancellationToken);
    }

    /// <summary>
    /// Loads a document only if it exists, is built-in, and is under the OGL license.
    /// Returns null otherwise (the controller maps null to 404).
    /// </summary>
    private async Task<SourceDocument?> GetOglDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await _db.SourceDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (doc is null || !doc.IsBuiltIn || !BuiltInLicenses.IsOgl(doc.LicenseKey))
            return null;

        return doc;
    }

    private static LicenseInfoDto MapToLicenseInfo(SourceDocument doc)
    {
        var license = BuiltInLicenses.TryResolve(doc.LicenseKey);
        return new LicenseInfoDto(
            DocumentId: doc.Id,
            DocumentTitle: doc.Title,
            LicenseKey: doc.LicenseKey,
            LicenseDisplayName: doc.LicenseDisplayName ?? license?.DisplayName,
            LicenseUrl: license?.Url,
            AttributionText: doc.AttributionText,
            AttributionUrl: doc.AttributionUrl,
            IsBuiltIn: doc.IsBuiltIn,
            IsOgl: BuiltInLicenses.IsOgl(doc.LicenseKey));
    }
}
