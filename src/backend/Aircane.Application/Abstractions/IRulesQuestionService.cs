using Aircane.Application.DTOs.Ai;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Service for answering rules questions using RAG-grounded retrieval and AI generation.
/// </summary>
public interface IRulesQuestionService
{
    /// <summary>
    /// Answers a rules question by retrieving relevant source chunks and generating
    /// an AI response grounded in those sources.
    /// </summary>
    /// <param name="request">The rules question request with optional context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer with citations and source support indicator.</returns>
    Task<RulesQuestionResponse> AskAsync(
        RulesQuestionRequest request,
        CancellationToken cancellationToken = default);
}
