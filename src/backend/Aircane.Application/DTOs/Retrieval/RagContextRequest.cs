using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Retrieval;

/// <summary>
/// Request parameters for building RAG context for an AI prompt.
/// </summary>
public sealed record RagContextRequest(
    /// <summary>The user's query or action text to find relevant context for.</summary>
    string Query,

    /// <summary>The game system to scope retrieval (e.g. "D&amp;D 5e 2014").</summary>
    string? GameSystem = null,

    /// <summary>The ruleset to scope retrieval (e.g. "PHB", "DMG").</summary>
    string? Ruleset = null,

    /// <summary>
    /// The role of the requester, used to determine maximum content visibility.
    /// Host/DM roles can see DMOnly content; players see only Public and Revealed.
    /// </summary>
    ParticipantRole RequesterRole = ParticipantRole.Host,

    /// <summary>Maximum number of chunks to retrieve before priority filtering.</summary>
    int TopK = 20,

    /// <summary>
    /// Maximum character budget for the assembled context string.
    /// Chunks are added in priority order until this budget is exhausted.
    /// </summary>
    int MaxContextChars = 8000,

    /// <summary>
    /// Minimum relevance score threshold for retrieved chunks.
    /// Chunks with a score below this value are discarded as low-confidence results.
    /// This prevents the AI from using poorly-matched content as source material.
    /// A value of 0.0 disables the filter. Default is 0.3.
    /// </summary>
    double MinRelevanceScore = 0.3,

    /// <summary>
    /// Candidate pool size to retrieve instead of <see cref="TopK"/> when reranking is active, so the
    /// reranker can choose from a wider set. Null means reranking does not change the pool size.
    /// </summary>
    int? RerankCandidateTopK = null,

    /// <summary>
    /// After reranking, keep only this many chunks (before deduplication). Null keeps them all.
    /// Ignored when reranking is not performed.
    /// </summary>
    int? PostRerankTopN = null,

    /// <summary>
    /// When true, reranking is bypassed even if a reranking provider is configured: the standard
    /// pool (<see cref="TopK"/>) and <see cref="MinRelevanceScore"/> apply, and no reranker is called.
    /// Used for interactive play, where the rerank latency is not acceptable.
    /// </summary>
    bool SkipReranking = false,

    /// <summary>
    /// The scope resolved from the campaign's game system definition, or null to filter by
    /// <see cref="GameSystem"/> and <see cref="Ruleset"/>. See <see cref="SearchRequest.Scope"/>.
    /// </summary>
    RetrievalScope? Scope = null);
