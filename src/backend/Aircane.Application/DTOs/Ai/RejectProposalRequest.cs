namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// Request to reject an AI action proposal.
/// </summary>
public sealed record RejectProposalRequest(
    Guid ProposalId,
    string? Reason = null);
