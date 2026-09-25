using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// License metadata for a built-in source document, used to enrich AI citations.
/// </summary>
public readonly record struct CitationLicense(
    string? LicenseKey,
    string? LicenseDisplayName,
    string? AttributionText);

/// <summary>
/// Looks up license/attribution metadata for cited source documents so AI citations
/// (rules-question and player-action) can surface it for built-in content. Shared to keep
/// the enrichment logic identical across the AI services.
/// </summary>
public static class CitationLicenseEnricher
{
    /// <summary>
    /// Returns a map of source-document ID to license metadata for the built-in documents among
    /// <paramref name="sourceDocumentIds"/>. Non-built-in or unknown IDs are simply absent from
    /// the map (callers treat a miss as "no license metadata").
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, CitationLicense>> BuildLicenseMapAsync(
        AircaneDbContext db,
        IEnumerable<Guid> sourceDocumentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = sourceDocumentIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, CitationLicense>();

        var rows = await db.SourceDocuments
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.IsBuiltIn)
            .Select(d => new
            {
                d.Id,
                d.LicenseKey,
                d.LicenseDisplayName,
                d.AttributionText,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => new CitationLicense(r.LicenseKey, r.LicenseDisplayName, r.AttributionText));
    }
}
