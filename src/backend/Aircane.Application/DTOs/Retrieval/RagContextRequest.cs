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
    double MinRelevanceScore = 0.3);
