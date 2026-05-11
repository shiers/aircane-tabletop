using Aircane.Application.DTOs.Ai;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Manages the AI action proposal queue — creating, listing, approving,
/// and rejecting proposals. On approval, applies the action to campaign state.
/// </summary>
public interface IAiProposalService
{
    /// <summary>
    /// Creates a new pending proposal from an AI-proposed action.
    /// </summary>
    Task<AiProposalDto> CreateProposalAsync(
        CreateProposalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all pending proposals for a given session.
    /// </summary>
    Task<IReadOnlyList<AiProposalDto>> GetPendingProposalsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a proposal and applies the action to campaign state.
    /// </summary>
    Task<AiProposalDto> ApproveProposalAsync(
        Guid proposalId,
        string approvedBy = "Host",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a proposal with an optional reason.
    /// </summary>
    Task<AiProposalDto> RejectProposalAsync(
        Guid proposalId,
        string rejectedBy = "Host",
        string? reason = null,
        CancellationToken cancellationToken = default);
}
