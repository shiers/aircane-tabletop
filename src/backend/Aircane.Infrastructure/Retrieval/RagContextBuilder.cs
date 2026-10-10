using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.Retrieval;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Retrieval.Reranking;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Builds compact, grounded prompt context for AI by retrieving relevant document chunks,
/// applying source priority and visibility filtering, and formatting citations.
/// </summary>
public sealed class RagContextBuilder : IRagContextBuilder
{
    private readonly IRetrievalService _retrievalService;
    private readonly IRerankingProvider _rerankingProvider;
    private readonly ILogger<RagContextBuilder> _logger;

    /// <summary>
    /// Source type priority order. Lower index = higher priority.
    /// Campaign house rules (Homebrew) > primary rules > adventure > other sources.
    /// </summary>
    private static readonly SourceType[] SourcePriorityOrder =
    [
        SourceType.Homebrew,   // Campaign house rules - highest priority
        SourceType.Rules,      // Primary rules sources (PHB, DMG, etc.)
        SourceType.Adventure,  // Adventure-specific content
        SourceType.Solo,       // Solo adventure content
        SourceType.Generated,  // AI-generated content
        SourceType.Character,  // Character-related content
        SourceType.Unknown     // Unclassified - lowest priority
    ];

    public RagContextBuilder(
        IRetrievalService retrievalService,
        ILogger<RagContextBuilder> logger,
        IRerankingProvider? rerankingProvider = null)
    {
        _retrievalService = retrievalService;
        _rerankingProvider = rerankingProvider ?? new NullRerankingProvider();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RagContextResult> BuildContextAsync(
        RagContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        // Determine maximum visibility based on requester role
        var maxVisibility = GetMaxVisibility(request.RequesterRole);

        // With reranking active, retrieve a wider candidate pool for the reranker to choose from.
        var rerankingActive = IsRerankingActive;
        var topK = rerankingActive ? request.RerankCandidateTopK ?? request.TopK : request.TopK;

        // Retrieve relevant chunks using the combined search
        var searchRequest = new SearchRequest(
            Query: request.Query,
            TopK: topK,
            GameSystem: request.GameSystem,
            Ruleset: request.Ruleset);

        var chunks = await _retrievalService.SearchAsync(
            searchRequest,
            maxVisibility,
            cancellationToken);

        if (chunks.Count == 0)
        {
            _logger.LogDebug("No chunks retrieved for query: {Query}", request.Query);
            return new RagContextResult(
                ContextText: string.Empty,
                Citations: [],
                TotalChunksRetrieved: 0,
                ChunksIncluded: 0);
        }

        // Filter out low-confidence chunks below the minimum relevance score. With reranking active the
        // floor is lowered (never raised) so the reranker, not the raw keyword/vector score, decides
        // what makes it into the context.
        var minScore = rerankingActive
            ? Math.Min(request.MinRelevanceScore, RerankingMinRelevanceScore)
            : request.MinRelevanceScore;
        var filteredChunks = FilterByMinRelevanceScore(chunks, minScore);

        if (filteredChunks.Count == 0)
        {
            _logger.LogDebug(
                "All {TotalChunks} chunks filtered out by minimum relevance score {MinScore} for query: {Query}",
                chunks.Count, minScore, request.Query);
            return new RagContextResult(
                ContextText: string.Empty,
                Citations: [],
                TotalChunksRetrieved: chunks.Count,
                ChunksIncluded: 0);
        }

        // Apply source priority ordering
        var prioritizedChunks = ApplySourcePriority(filteredChunks);

        // Optionally rerank against the full question (cross-encoder), before dedup so the
        // best-scoring copy of a duplicated passage is the one kept.
        prioritizedChunks = await RerankAsync(
            request.Query, prioritizedChunks, request.PostRerankTopN, cancellationToken);

        // Drop near-duplicate chunks so overlapping text doesn't waste the character budget
        var distinctChunks = RemoveDuplicateChunks(prioritizedChunks);

        // Build compact context within character budget
        var (contextText, citations) = BuildCompactContext(distinctChunks, request.MaxContextChars);

        _logger.LogDebug(
            "Built RAG context: {ChunksIncluded}/{TotalChunks} chunks, {ContextLength} chars for query: {Query}",
            citations.Count, chunks.Count, contextText.Length, request.Query);

        return new RagContextResult(
            ContextText: contextText,
            Citations: citations,
            TotalChunksRetrieved: chunks.Count,
            ChunksIncluded: citations.Count);
    }

    /// <summary>
    /// Minimum retrieval score when reranking is active. Low enough to give the reranker a wide pool,
    /// high enough to keep completely irrelevant chunks out of it.
    /// </summary>
    public const double RerankingMinRelevanceScore = 0.15;

    /// <summary>True when a non-"None" reranking provider is configured and usable.</summary>
    private bool IsRerankingActive =>
        _rerankingProvider.ProviderId != NullRerankingProvider.Id && _rerankingProvider.IsAvailable;

    /// <summary>Additive score boost for house rules over an equally relevant core-rules chunk.</summary>
    public const float HomebrewRerankBoost = 0.10f;

    /// <summary>Additive score boost for primary rules sources.</summary>
    public const float RulesRerankBoost = 0.05f;

    /// <summary>The additive boost applied to a chunk's rerank score, by source type.</summary>
    public static float GetSourceBoost(SourceType sourceType) => sourceType switch
    {
        SourceType.Homebrew => HomebrewRerankBoost,
        SourceType.Rules => RulesRerankBoost,
        _ => 0f,
    };

    /// <summary>
    /// Reorders <paramref name="chunks"/> by the reranking provider's relevance score plus a small
    /// source-type boost (<see cref="GetSourceBoost"/>), then keeps the top
    /// <paramref name="postRerankTopN"/>. Skipped entirely, with no boost and no truncation, when the
    /// provider is "None", unavailable, or there is nothing to reorder. Chunks the provider does not
    /// score get a base score of 0 (plus their boost).
    /// </summary>
    /// <remarks>
    /// The boost keeps the reranker's relevance ordering but lets a highly relevant Homebrew chunk
    /// outrank an equally relevant Rules chunk, so house rules are not lost to core rules.
    /// </remarks>
    private async Task<IReadOnlyList<ChunkResultDto>> RerankAsync(
        string query,
        IReadOnlyList<ChunkResultDto> chunks,
        int? postRerankTopN,
        CancellationToken cancellationToken)
    {
        if (chunks.Count <= 1 || !IsRerankingActive)
            return chunks;

        var candidates = chunks
            .Select(c => new RerankCandidate(c.ChunkId.ToString(), c.Text))
            .ToList();

        var reranked = await _rerankingProvider.RerankAsync(query, candidates, cancellationToken);

        var scoreMap = reranked.ToDictionary(r => r.Id, r => r.Score);
        var ordered = chunks
            .OrderByDescending(c => scoreMap.GetValueOrDefault(c.ChunkId.ToString(), 0f) + GetSourceBoost(c.SourceType))
            .AsEnumerable();

        // Truncate before dedup so the context stays focused on the best few.
        if (postRerankTopN is > 0)
            ordered = ordered.Take(postRerankTopN.Value);

        return ordered.ToList();
    }

    /// <summary>
    /// Determines the maximum content visibility level based on the requester's role.
    /// </summary>
    public static ContentVisibility GetMaxVisibility(ParticipantRole role)
    {
        return role switch
        {
            ParticipantRole.Host => ContentVisibility.DMOnly,
            ParticipantRole.HumanDm => ContentVisibility.DMOnly,
            ParticipantRole.Player => ContentVisibility.Public,
            ParticipantRole.Spectator => ContentVisibility.Public,
            _ => ContentVisibility.Public
        };
    }

    /// <summary>
    /// Sorts chunks by source type priority (see <see cref="SourcePriorityOrder"/>), ordering by
    /// relevance score within the same priority tier.
    /// </summary>
    public static IReadOnlyList<ChunkResultDto> ApplySourcePriority(IReadOnlyList<ChunkResultDto> chunks)
    {
        return chunks
            .OrderBy(c => GetSourceTypePriority(c.SourceType))
            .ThenByDescending(c => c.Score ?? 0)
            .ToList();
    }

    /// <summary>Length of the substrings compared when measuring overlap between two chunks.</summary>
    private const int OverlapWindowChars = 50;

    /// <summary>A chunk is dropped when more than this fraction of its text repeats an earlier chunk.</summary>
    private const double DuplicateOverlapThreshold = 0.40;

    /// <summary>
    /// Walks the chunks in order and skips any whose text overlaps an already-kept chunk by more than
    /// <see cref="DuplicateOverlapThreshold"/> of the shorter chunk's length. Earlier chunks win, so
    /// the higher-priority / higher-scoring copy is the one kept.
    /// </summary>
    public static IReadOnlyList<ChunkResultDto> RemoveDuplicateChunks(IReadOnlyList<ChunkResultDto> chunks)
    {
        var kept = new List<ChunkResultDto>(chunks.Count);
        var keptTexts = new List<string>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var text = NormalizeWhitespace(chunk.Text);
            if (keptTexts.Any(existing => OverlapFraction(text, existing) > DuplicateOverlapThreshold))
                continue;

            kept.Add(chunk);
            keptTexts.Add(text);
        }

        return kept;
    }

    /// <summary>
    /// Approximates <c>overlap_chars / min(len_a, len_b)</c> by counting the 50-character windows of
    /// the shorter text that also occur in the longer one. Each matching window position stands for
    /// one overlapping character.
    /// </summary>
    internal static double OverlapFraction(string a, string b)
    {
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shorter.Length == 0)
            return 0;

        // Too short for windowing: duplicate only if wholly contained in the other text.
        if (shorter.Length < OverlapWindowChars)
            return longer.Contains(shorter, StringComparison.Ordinal) ? 1.0 : 0.0;

        var longerWindows = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + OverlapWindowChars <= longer.Length; i++)
            longerWindows.Add(longer.Substring(i, OverlapWindowChars));

        var shorterWindowCount = shorter.Length - OverlapWindowChars + 1;
        var matching = 0;
        for (var i = 0; i < shorterWindowCount; i++)
        {
            if (longerWindows.Contains(shorter.Substring(i, OverlapWindowChars)))
                matching++;
        }

        // A run of k matching windows covers k + 49 characters; approximate the overlapping
        // characters as the matching windows plus that tail when anything matched.
        var overlapChars = matching == 0 ? 0 : matching + OverlapWindowChars - 1;
        return Math.Min(1.0, (double)overlapChars / shorter.Length);
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Filters out chunks whose relevance score is below the specified minimum threshold.
    /// Chunks with a null score are retained (they may come from keyword-only search).
    /// </summary>
    /// <param name="chunks">The chunks to filter.</param>
    /// <param name="minScore">The minimum relevance score threshold. A value of 0.0 disables filtering.</param>
    /// <returns>Chunks that meet or exceed the minimum score threshold.</returns>
    public static IReadOnlyList<ChunkResultDto> FilterByMinRelevanceScore(
        IReadOnlyList<ChunkResultDto> chunks,
        double minScore)
    {
        if (minScore <= 0.0)
            return chunks;

        return chunks
            .Where(c => c.Score is null || c.Score >= minScore)
            .ToList();
    }

    /// <summary>
    /// Sorts chunks by source type priority, preserving relevance order within the same priority tier.
    /// This overload accepts chunks with explicit source type information.
    /// </summary>
    public static IReadOnlyList<PrioritizedChunk> ApplySourcePriorityWithType(
        IReadOnlyList<PrioritizedChunk> chunks)
    {
        return chunks
            .OrderBy(c => GetSourceTypePriority(c.SourceType))
            .ThenByDescending(c => c.Score ?? 0)
            .ToList();
    }

    /// <summary>
    /// Gets the numeric priority for a source type (lower = higher priority).
    /// </summary>
    public static int GetSourceTypePriority(SourceType sourceType)
    {
        var index = Array.IndexOf(SourcePriorityOrder, sourceType);
        return index >= 0 ? index : SourcePriorityOrder.Length;
    }

    /// <summary>
    /// Builds a compact context string with citations, respecting the character budget.
    /// </summary>
    private static (string contextText, IReadOnlyList<RagCitation> citations) BuildCompactContext(
        IReadOnlyList<ChunkResultDto> chunks,
        int maxChars)
    {
        var sb = new StringBuilder();
        var citations = new List<RagCitation>();
        var citationIndex = 1;

        sb.AppendLine("## Retrieved Rules Context");
        sb.AppendLine();

        foreach (var chunk in chunks)
        {
            // Format the chunk entry with citation marker
            var chunkEntry = FormatChunkEntry(chunk, citationIndex);

            // Check if adding this chunk would exceed the budget
            if (sb.Length + chunkEntry.Length > maxChars)
                break;

            sb.Append(chunkEntry);
            citations.Add(new RagCitation(
                ChunkId: chunk.ChunkId,
                SourceDocumentId: chunk.SourceDocumentId,
                SourceDocumentTitle: chunk.SourceDocumentTitle,
                PageNumber: chunk.PageNumber,
                SectionTitle: chunk.SectionTitle));

            citationIndex++;
        }

        // Append citation summary at the end
        if (citations.Count > 0)
        {
            var citationSummary = FormatCitationSummary(citations);
            // Only append if it fits within budget
            if (sb.Length + citationSummary.Length <= maxChars)
            {
                sb.Append(citationSummary);
            }
        }

        return (sb.ToString().TrimEnd(), citations);
    }

    /// <summary>
    /// Formats a single chunk entry for inclusion in the prompt context.
    /// </summary>
    private static string FormatChunkEntry(ChunkResultDto chunk, int citationIndex)
    {
        var sb = new StringBuilder();
        sb.Append($"[{citationIndex}] ");

        // Add section/page reference if available
        if (!string.IsNullOrWhiteSpace(chunk.SectionTitle))
        {
            sb.Append($"({chunk.SourceDocumentTitle} - {chunk.SectionTitle}");
            if (chunk.PageNumber.HasValue)
                sb.Append($", p.{chunk.PageNumber}");
            sb.AppendLine(")");
        }
        else if (chunk.PageNumber.HasValue)
        {
            sb.AppendLine($"({chunk.SourceDocumentTitle}, p.{chunk.PageNumber})");
        }
        else
        {
            sb.AppendLine($"({chunk.SourceDocumentTitle})");
        }

        sb.AppendLine(chunk.Text.Trim());
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Formats a citation summary section for the end of the context.
    /// </summary>
    private static string FormatCitationSummary(IReadOnlyList<RagCitation> citations)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Sources:");

        for (var i = 0; i < citations.Count; i++)
        {
            var c = citations[i];
            var location = FormatCitationLocation(c);
            sb.AppendLine($"  [{i + 1}] {c.SourceDocumentTitle}{location}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the location portion of a citation (page/section).
    /// </summary>
    private static string FormatCitationLocation(RagCitation citation)
    {
        if (!string.IsNullOrWhiteSpace(citation.SectionTitle) && citation.PageNumber.HasValue)
            return $" - {citation.SectionTitle}, p.{citation.PageNumber}";
        if (!string.IsNullOrWhiteSpace(citation.SectionTitle))
            return $" - {citation.SectionTitle}";
        if (citation.PageNumber.HasValue)
            return $", p.{citation.PageNumber}";
        return string.Empty;
    }
}

/// <summary>
/// DTO for chunks with explicit source type information for priority sorting.
/// </summary>
public sealed record PrioritizedChunk(
    Guid ChunkId,
    Guid SourceDocumentId,
    string SourceDocumentTitle,
    int ChunkIndex,
    string Text,
    int? PageNumber,
    string? SectionTitle,
    ContentVisibility Visibility,
    double? Score,
    SourceType SourceType);
