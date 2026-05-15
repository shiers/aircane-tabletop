using System.Text;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Builds compact, grounded prompt context for AI by retrieving relevant document chunks,
/// applying source priority and visibility filtering, and formatting citations.
/// </summary>
public sealed class RagContextBuilder : IRagContextBuilder
{
    private readonly IRetrievalService _retrievalService;
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
        ILogger<RagContextBuilder> logger)
    {
        _retrievalService = retrievalService;
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

        // Retrieve relevant chunks using the combined search
        var searchRequest = new SearchRequest(
            Query: request.Query,
            TopK: request.TopK,
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

        // Filter out low-confidence chunks below the minimum relevance score
        var filteredChunks = FilterByMinRelevanceScore(chunks, request.MinRelevanceScore);

        if (filteredChunks.Count == 0)
        {
            _logger.LogDebug(
                "All {TotalChunks} chunks filtered out by minimum relevance score {MinScore} for query: {Query}",
                chunks.Count, request.MinRelevanceScore, request.Query);
            return new RagContextResult(
                ContextText: string.Empty,
                Citations: [],
                TotalChunksRetrieved: chunks.Count,
                ChunksIncluded: 0);
        }

        // Apply source priority ordering
        var prioritizedChunks = ApplySourcePriority(filteredChunks);

        // Build compact context within character budget
        var (contextText, citations) = BuildCompactContext(prioritizedChunks, request.MaxContextChars);

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
    /// Sorts chunks by source type priority, preserving relevance order within the same priority tier.
    /// </summary>
    public static IReadOnlyList<ChunkResultDto> ApplySourcePriority(IReadOnlyList<ChunkResultDto> chunks)
    {
        // We need to infer source type from the chunk. Since ChunkResultDto doesn't carry SourceType directly,
        // we use the document title as a proxy for grouping. The retrieval service already returns
        // chunks with their source document title. For proper priority, we'd need SourceType on the DTO.
        // For now, we preserve the retrieval order (which already includes relevance scoring)
        // and rely on the retrieval service's built-in priority ranking.
        //
        // If ChunkResultDto is extended with SourceType in the future, this method can apply
        // explicit priority sorting.
        return chunks;
    }

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
