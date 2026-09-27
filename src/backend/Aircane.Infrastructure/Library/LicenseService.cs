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
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(
        AircaneDbContext db,
        ILogger<LicenseService> logger)
    {
        _db = db;
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
            IsBuiltIn: doc.IsBuiltIn);
    }
}
