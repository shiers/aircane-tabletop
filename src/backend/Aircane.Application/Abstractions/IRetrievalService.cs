using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Enums;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Performs keyword and vector retrieval over indexed document chunks,
/// with visibility filtering and priority ranking.
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Searches chunks using full-text keyword matching.
    /// Applies visibility filtering based on the caller's role.
    /// </summary>
    Task<IReadOnlyList<ChunkResultDto>> SearchByKeywordAsync(
        SearchRequest request,
        ContentVisibility maxVisibility,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches chunks using vector similarity against the query embedding.
    /// Applies visibility filtering based on the caller's role.
    /// </summary>
    Task<IReadOnlyList<ChunkResultDto>> SearchByVectorAsync(
        SearchRequest request,
        ContentVisibility maxVisibility,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a combined keyword and vector search, merges results,
    /// applies visibility filtering, and returns ranked chunks.
    /// </summary>
    Task<IReadOnlyList<ChunkResultDto>> SearchAsync(
        SearchRequest request,
        ContentVisibility maxVisibility,
        CancellationToken cancellationToken = default);
}
