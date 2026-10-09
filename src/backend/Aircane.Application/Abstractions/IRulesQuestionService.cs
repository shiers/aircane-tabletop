using Aircane.Application.DTOs.Ai;
using Aircane.Domain.Enums;

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
    /// <param name="requesterRole">
    /// The caller's role, which bounds the content visibility of retrieved sources. Defaults to the
    /// least-privileged role so callers must opt in to DM-only content.
    /// </param>
    /// <returns>The answer with citations and source support indicator.</returns>
    Task<RulesQuestionResponse> AskAsync(
        RulesQuestionRequest request,
        CancellationToken cancellationToken = default,
        ParticipantRole requesterRole = ParticipantRole.Player);
}
