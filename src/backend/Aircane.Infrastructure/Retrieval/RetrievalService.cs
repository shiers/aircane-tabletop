using System.Linq.Expressions;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;

namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Implements keyword and vector retrieval over indexed document chunks,
/// with visibility filtering and priority ranking.
/// </summary>
public sealed class RetrievalService : IRetrievalService
{
    // Score weights used when merging keyword and vector results (see SearchAsync).
    private const double KeywordOnlyWeight = 0.7;
    private const double VectorOnlyWeight = 0.6;
    private const double HybridVectorWeight = 0.5;

    private readonly AircaneDbContext _db;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IEmbeddingCompatibility? _embeddingCompatibility;
    private readonly IGameSystemCanonicalizer? _gameSystemCanonicalizer;
    private readonly ILogger<RetrievalService> _logger;

    public RetrievalService(
        AircaneDbContext db,
        IEmbeddingProvider embeddingProvider,
        IEmbeddingCompatibility? embeddingCompatibility = null,
        ILogger<RetrievalService>? logger = null,
        IGameSystemCanonicalizer? gameSystemCanonicalizer = null)
    {
        _db = db;
        _embeddingProvider = embeddingProvider;
        _embeddingCompatibility = embeddingCompatibility;
        _logger = logger ?? NullLogger<RetrievalService>.Instance;
        _gameSystemCanonicalizer = gameSystemCanonicalizer;
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

        // Match chunks containing ANY of the question's terms, ranked by how many terms they
        // contain. Matching the whole question as one substring would almost never hit for a
        // natural-language question ("explain actor skill").
        var terms = KeywordQueryParser.ExtractTerms(request.Query);

        var filter = await ResolveFilterAsync(request, cancellationToken);
        var query = BuildFilteredQuery(request, filter, allowedVisibilities);

        var results = await query
            .Where(BuildAnyTermMatch(terms))
            .OrderByDescending(BuildTermHitCount(terms))
            .ThenBy(c => c.Chunk.SourceDocumentId)
            .ThenBy(c => c.Chunk.ChunkIndex)
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
                null,
                c.Doc.SourceType))
            .ToListAsync(cancellationToken);

        // Score = fraction of the query's terms the chunk contains (0..1].
        return results
            .Select(r => r with { Score = (double)CountTermHits(r, terms) / terms.Count })
            .ToList();
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

        // If the active provider's embedding dimension doesn't match the pgvector column, vector
        // search is disabled (the vectors are incomparable and can't be stored). Keyword search
        // continues to work via SearchByKeywordAsync.
        if (_embeddingCompatibility is { IsVectorSearchEnabled: false })
        {
            _logger.LogDebug(
                "Vector search skipped: embedding dimension ({ProviderDim}) does not match the column ({ColumnDim}).",
                _embeddingCompatibility.ProviderDimension, _embeddingCompatibility.ColumnDimension);
            return [];
        }

        var embedding = await _embeddingProvider.GenerateEmbeddingAsync(request.Query, cancellationToken);
        var vector = new Pgvector.Vector(embedding);

        var allowedVisibilities = GetAllowedVisibilities(maxVisibility);
        var filter = await ResolveFilterAsync(request, cancellationToken);
        var query = BuildFilteredQuery(request, filter, allowedVisibilities);

        var activeProvider = _embeddingProvider.ProviderName;
        var activeModel = _embeddingProvider.ModelName;
        var activeDimensions = _embeddingProvider.Dimensions;

        // Embeddings are only comparable when produced by the same provider + model + dimension
        // as the active query embedding. Mixing embedding spaces yields meaningless cosine
        // distances, so restrict vector search to chunks whose recorded provenance matches the
        // active provider. Chunks with NULL provenance predate provenance tracking and are treated
        // as compatible (they were embedded by whatever provider was active at the time).

        // Diagnostic: warn (do not silently ignore) when embedded chunks are excluded because
        // their provenance does not match the active provider — this is the "re-index required"
        // signal that prevents silently-wrong retrieval after a provider/model change.
        var mismatchedCount = await query
            .Where(c => c.Chunk.Embedding != null
                && c.Chunk.EmbeddingProvider != null
                && (c.Chunk.EmbeddingProvider != activeProvider
                    || c.Chunk.EmbeddingModel != activeModel
                    || c.Chunk.EmbeddingDimensions != activeDimensions))
            .CountAsync(cancellationToken);

        if (mismatchedCount > 0)
        {
            _logger.LogWarning(
                "Vector search skipped {Count} embedded chunk(s) whose embedding provenance does not " +
                "match the active provider ({Provider}/{Model}, {Dimensions}d). Re-index those documents " +
                "to make them searchable with the current embedding provider.",
                mismatchedCount, activeProvider, activeModel, activeDimensions);
        }

        // Cosine distance via pgvector: lower distance = more similar
        var results = await query
            .Where(c => c.Chunk.Embedding != null
                && (c.Chunk.EmbeddingProvider == null
                    || (c.Chunk.EmbeddingProvider == activeProvider
                        && c.Chunk.EmbeddingModel == activeModel
                        && c.Chunk.EmbeddingDimensions == activeDimensions)))
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
                (double?)c.Chunk.Embedding!.CosineDistance(vector),
                c.Doc.SourceType))
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

        // Run searches sequentially — DbContext is not thread-safe and cannot
        // handle concurrent operations on the same instance.
        var keywordResults = await SearchByKeywordAsync(request, maxVisibility, cancellationToken);
        var vectorResults = await SearchByVectorAsync(request, maxVisibility, cancellationToken);

        // Merge and deduplicate by ChunkId. Exact terminology is more reliable than semantic
        // similarity for rules lookups ("grapple" and "grab" are close in embedding space but
        // mechanically distinct), so keyword matches are weighted above vector-only matches.
        var merged = new Dictionary<Guid, ChunkResultDto>();
        var keywordScores = new Dictionary<Guid, double>();

        foreach (var result in keywordResults)
        {
            // result.Score is the fraction of query terms matched, so partial matches rank below
            // full ones.
            var fraction = result.Score ?? 1.0;
            keywordScores[result.ChunkId] = fraction;
            merged[result.ChunkId] = result with { Score = KeywordOnlyWeight * fraction };
        }

        foreach (var result in vectorResults)
        {
            var similarity = result.Score ?? 0.0;

            if (keywordScores.TryGetValue(result.ChunkId, out var fraction))
            {
                // Found by both methods: combine the weighted contributions.
                var combinedScore = Math.Min(1.0, KeywordOnlyWeight * fraction + HybridVectorWeight * similarity);
                merged[result.ChunkId] = merged[result.ChunkId] with { Score = combinedScore };
            }
            else
            {
                merged[result.ChunkId] = result with { Score = VectorOnlyWeight * similarity };
            }
        }

        return merged.Values
            .OrderByDescending(r => r.Score ?? 0.0)
            .Take(request.TopK)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAvailableRulesetsAsync(
        string? gameSystem,
        CancellationToken cancellationToken = default)
    {
        var gameSystems = NormalizeGameSystems(await ResolveGameSystemsAsync(gameSystem, cancellationToken));

        var query = _db.SourceDocuments.Where(d => !d.IsDisabled && d.Ruleset != "");
        if (gameSystems.Count > 0)
            query = query.Where(d => gameSystems.Contains(d.GameSystem.Trim().ToLower()));

        var rulesets = await query
            .Select(d => d.Ruleset.Trim())
            .Distinct()
            .ToListAsync(cancellationToken);

        return rulesets
            .Where(r => r.Length > 0)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
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
        RetrievalFilter filter,
        IReadOnlyList<ContentVisibility> allowedVisibilities)
    {
        var gameSystems = filter.GameSystems;
        var rulesets = filter.Rulesets;

        var query = _db.DocumentChunks
            .Join(
                _db.SourceDocuments,
                chunk => chunk.SourceDocumentId,
                doc => doc.Id,
                (chunk, doc) => new ChunkWithDoc { Chunk = chunk, Doc = doc })
            .Where(c => allowedVisibilities.Contains(c.Chunk.Visibility))
            // Exclude chunks belonging to disabled documents (e.g. host-disabled built-in content).
            .Where(c => !c.Doc.IsDisabled);

        if (request.SourceType.HasValue)
            query = query.Where(c => c.Doc.SourceType == request.SourceType.Value);

        // Case-insensitive. A document with a blank game system never matches a game-system filter.
        if (gameSystems.Count > 0)
            query = query.Where(c => gameSystems.Contains(c.Doc.GameSystem.Trim().ToLower()));

        // Case- and whitespace-insensitive, and expanded to known aliases ("2014" == "D&D 5e 2014").
        // For a definition scope, a document with a blank ruleset (edition unspecified) is included:
        // its game system already matched above.
        if (rulesets.Count > 0)
        {
            query = filter.IncludeBlankRuleset
                ? query.Where(c => rulesets.Contains(c.Doc.Ruleset.Trim().ToLower()) || c.Doc.Ruleset.Trim() == "")
                : query.Where(c => rulesets.Contains(c.Doc.Ruleset.Trim().ToLower()));
        }

        return query;
    }

    /// <summary>The metadata filter a search applies: lower-cased game systems and normalized rulesets.</summary>
    private sealed record RetrievalFilter(
        IReadOnlyList<string> GameSystems,
        IReadOnlyList<string> Rulesets,
        bool IncludeBlankRuleset);

    /// <summary>
    /// Uses the request's definition scope when it has one; otherwise the request's free-text game
    /// system (expanded through the canonicalizer) and ruleset (expanded through the alias groups).
    /// </summary>
    private async Task<RetrievalFilter> ResolveFilterAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (request.Scope is { } scope)
            return new RetrievalFilter(NormalizeGameSystems(scope.GameSystems), scope.Rulesets, IncludeBlankRuleset: true);

        var gameSystems = await ResolveGameSystemsAsync(request.GameSystem, cancellationToken);
        var rulesets = string.IsNullOrWhiteSpace(request.Ruleset)
            ? []
            : RulesetNormalizer.GetEquivalents(request.Ruleset);

        return new RetrievalFilter(NormalizeGameSystems(gameSystems), rulesets, IncludeBlankRuleset: false);
    }

    /// <summary>Trims and lower-cases game-system spellings for a case-insensitive comparison.</summary>
    private static IReadOnlyList<string> NormalizeGameSystems(IEnumerable<string> gameSystems) =>
        gameSystems
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Case-insensitive substring match of one term against a chunk's text or section title.
    /// Using ToLower().Contains(...) instead of EF.Functions.ILike keeps the query
    /// provider-agnostic: Npgsql translates it to a server-side lower(...) LIKE '%term%', and the
    /// EF Core InMemory provider (used by tests) can evaluate it as well. Contains also treats
    /// % and _ as literals, avoiding accidental LIKE-wildcard behavior from user queries.
    /// </summary>
    private static Expression<Func<ChunkWithDoc, bool>> TermMatch(string term) =>
        c => c.Chunk.Text.ToLower().Contains(term) ||
             (c.Chunk.SectionTitle != null && c.Chunk.SectionTitle.ToLower().Contains(term));

    /// <summary>Builds <c>c =&gt; match(term1) || match(term2) || ...</c>.</summary>
    private static Expression<Func<ChunkWithDoc, bool>> BuildAnyTermMatch(IReadOnlyList<string> terms)
    {
        var parameter = Expression.Parameter(typeof(ChunkWithDoc), "c");
        var body = terms
            .Select(t => ParameterRebinder.Rebind(TermMatch(t), parameter))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<ChunkWithDoc, bool>>(body, parameter);
    }

    /// <summary>Builds <c>c =&gt; (match(term1) ? 1 : 0) + (match(term2) ? 1 : 0) + ...</c>.</summary>
    private static Expression<Func<ChunkWithDoc, int>> BuildTermHitCount(IReadOnlyList<string> terms)
    {
        var parameter = Expression.Parameter(typeof(ChunkWithDoc), "c");
        var body = terms
            .Select(t => (Expression)Expression.Condition(
                ParameterRebinder.Rebind(TermMatch(t), parameter),
                Expression.Constant(1),
                Expression.Constant(0)))
            .Aggregate(Expression.Add);
        return Expression.Lambda<Func<ChunkWithDoc, int>>(body, parameter);
    }

    /// <summary>In-memory equivalent of <see cref="BuildTermHitCount"/> for scoring results.</summary>
    private static int CountTermHits(ChunkResultDto result, IReadOnlyList<string> terms) =>
        terms.Count(t =>
            result.Text.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            (result.SectionTitle?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));

    /// <summary>Substitutes a lambda's parameter so several lambdas can share one parameter.</summary>
    private sealed class ParameterRebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        private ParameterRebinder(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        public static Expression Rebind<T>(Expression<T> lambda, ParameterExpression to) =>
            new ParameterRebinder(lambda.Parameters[0], to).Visit(lambda.Body);

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    /// <summary>
    /// Expands a requested game system into every spelling a document might be stored under
    /// (definition name, identifier, aliases), so a filter of "D&amp;D 5e" matches documents
    /// canonicalized to the definition name and vice versa. Without a canonicalizer, the filter is
    /// an exact match on the requested value.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveGameSystemsAsync(
        string? gameSystem,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameSystem))
            return [];

        if (_gameSystemCanonicalizer is null)
            return [gameSystem];

        return await _gameSystemCanonicalizer.GetEquivalentNamesAsync(gameSystem, cancellationToken);
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
