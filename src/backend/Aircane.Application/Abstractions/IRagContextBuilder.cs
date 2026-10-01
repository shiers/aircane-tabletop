using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Enums;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Builds compact, grounded prompt context for AI by retrieving relevant document chunks,
/// applying source priority and visibility filtering, and formatting citations.
/// </summary>
public interface IRagContextBuilder
{
    /// <summary>
    /// Retrieves relevant chunks for the given query, applies source priority and visibility,
    /// and builds a compact prompt context string with source citations.
    /// </summary>
    /// <param name="request">The context request containing query, campaign/ruleset context, and role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A built context result containing the prompt text and citation metadata.</returns>
    Task<RagContextResult> BuildContextAsync(
        RagContextRequest request,
        CancellationToken cancellationToken = default);
}
