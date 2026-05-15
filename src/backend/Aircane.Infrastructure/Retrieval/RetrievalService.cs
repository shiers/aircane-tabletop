using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Implements keyword and vector retrieval over indexed document chunks,
/// with visibility filtering and priority ranking.
/// </summary>
public sealed class RetrievalService : IRetrievalService
{
    private readonly AircaneDbContext _db;
    private readonly IEmbeddingProvider _embeddingProvider;

    public RetrievalService(AircaneDbContext db, IEmbeddingProvider embeddingProvider)
    {
        _db = db;
        _embeddingProvider = embeddingProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkResultDto>> SearchByKeywordAsync(
        SearchRequest request,
        ContentVisibility maxVisibility,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return [];

        var allowedVisibilities = GetAllowedVisibilities(maxVisibility);
        var lowerQuery = request.Query.ToLower();

        var query = BuildFilteredQuery(request, allowedVisibilities);

        var results = await query
            .Where(c =>
                c.Chunk.Text.ToLower().Contains(lowerQuery) ||
                (c.Chunk.SectionTitle != null && c.Chunk.SectionTitle.ToLower().Contains(lowerQuery)))
            .Take(request.TopK)
            .Select(c => new ChunkResultDto(
                c.Chunk.Id,
                c.Doc.Id,
                c.Doc.Title,
                c.Chunk.ChunkIndex,
                c.Chunk.Text,
                c.Chunk.PageNumber,
                c.Chunk.SectionTitle,
                c.Chunk.Visibility,
                null))
            .ToListAsync(cancellationToken);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkResultDto>> SearchByVectorAsync(
        SearchRequest request,
        ContentVisibility maxVisibility,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return [];

        // The Embedding property is ignored in the in-memory provider (see AircaneDbContext).
        // Vector search requires PostgreSQL with pgvector; return empty for in-memory contexts.
        var providerName = _db.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            return [];

        var embedding = await _embeddingProvider.GenerateEmbeddingAsync(request.Query, cancellationToken);
        var vector = new Pgvector.Vector(embedding);

        var allowedVisibilities = GetAllowedVisibilities(maxVisibility);
        var query = BuildFilteredQuery(request, allowedVisibilities);

        // Cosine distance via pgvector: lower distance = more similar
        var results = await query
            .Where(c => c.Chunk.Embedding != null)
            .OrderBy(c => c.Chunk.Embedding!.CosineDistance(vector))
            .Take(request.TopK)
            .Select(c => new ChunkResultDto(
                c.Chunk.Id,
                c.Doc.Id,
                c.Doc.Title,
                c.Chunk.ChunkIndex,
                c.Chunk.Text,
                c.Chunk.PageNumber,
                c.Chunk.SectionTitle,
                c.Chunk.Visibility,
                (double?)c.Chunk.Embedding!.CosineDistance(vector)))
            .ToListAsync(cancellationToken);

        // Convert cosine distance to similarity score (1 - distance)
        return results
            .Select(r => r with { Score = r.Score.HasValue ? 1.0 - r.Score.Value : null })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkResultDto>> SearchAsync(
        SearchRequest request,
        ContentVisibility maxVisibility,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return [];

        var keywordTask = SearchByKeywordAsync(request, maxVisibility, cancellationToken);
        var vectorTask = SearchByVectorAsync(request, maxVisibility, cancellationToken);

        await Task.WhenAll(keywordTask, vectorTask);

        var keywordResults = await keywordTask;
        var vectorResults = await vectorTask;

        // Merge and deduplicate by ChunkId, preferring the higher score
        var merged = new Dictionary<Guid, ChunkResultDto>();

        foreach (var result in keywordResults)
        {
            // Keyword results get a base score of 0.5 (present = relevant)
            var scored = result with { Score = 0.5 };
            merged[result.ChunkId] = scored;
        }

        foreach (var result in vectorResults)
        {
            if (merged.TryGetValue(result.ChunkId, out var existing))
            {
                // Boost score when found by both methods
                var combinedScore = Math.Min(1.0, (existing.Score ?? 0.5) + (result.Score ?? 0.0) * 0.5);
                merged[result.ChunkId] = existing with { Score = combinedScore };
            }
            else
            {
                merged[result.ChunkId] = result;
            }
        }

        return merged.Values
            .OrderByDescending(r => r.Score ?? 0.0)
            .Take(request.TopK)
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// A projection type used internally to carry both chunk and document
    /// through the EF Core query pipeline without using ValueTuple,
    /// which the in-memory provider cannot translate.
    /// </summary>
    private sealed class ChunkWithDoc
    {
        public DocumentChunk Chunk { get; init; } = null!;
        public SourceDocument Doc { get; init; } = null!;
    }

    /// <summary>
    /// Builds the base joined query with metadata filters and visibility filtering applied.
    /// Returns an <see cref="IQueryable{ChunkWithDoc}"/> that can be further filtered.
    /// </summary>
    private IQueryable<ChunkWithDoc> BuildFilteredQuery(
        SearchRequest request,
        IReadOnlyList<ContentVisibility> allowedVisibilities)
    {
        var query = _db.DocumentChunks
            .Join(
                _db.SourceDocuments,
                chunk => chunk.SourceDocumentId,
                doc => doc.Id,
                (chunk, doc) => new ChunkWithDoc { Chunk = chunk, Doc = doc })
            .Where(c => allowedVisibilities.Contains(c.Chunk.Visibility));

        if (request.SourceType.HasValue)
            query = query.Where(c => c.Doc.SourceType == request.SourceType.Value);

        if (!string.IsNullOrWhiteSpace(request.GameSystem))
            query = query.Where(c => c.Doc.GameSystem == request.GameSystem);

        if (!string.IsNullOrWhiteSpace(request.Ruleset))
            query = query.Where(c => c.Doc.Ruleset == request.Ruleset);

        return query;
    }

    /// <summary>
    /// Returns the set of <see cref="ContentVisibility"/> values visible to a caller
    /// with the given <paramref name="maxVisibility"/> level.
    /// <list type="bullet">
    ///   <item><see cref="ContentVisibility.Public"/> - only Public chunks</item>
    ///   <item><see cref="ContentVisibility.Revealed"/> - Public and Revealed chunks (player-visible)</item>
    ///   <item><see cref="ContentVisibility.DMOnly"/> - Public, Revealed, and DMOnly chunks (DM/Host)</item>
    ///   <item><see cref="ContentVisibility.Hidden"/> - all chunks including Hidden (Host/admin)</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<ContentVisibility> GetAllowedVisibilities(ContentVisibility maxVisibility)
    {
        return maxVisibility switch
        {
            ContentVisibility.Public => [ContentVisibility.Public],
            ContentVisibility.Revealed => [ContentVisibility.Public, ContentVisibility.Revealed],
            ContentVisibility.DMOnly => [ContentVisibility.Public, ContentVisibility.Revealed, ContentVisibility.DMOnly],
            ContentVisibility.Hidden => [ContentVisibility.Public, ContentVisibility.Revealed, ContentVisibility.DMOnly, ContentVisibility.Hidden],
            _ => [ContentVisibility.Public]
        };
    }
}
