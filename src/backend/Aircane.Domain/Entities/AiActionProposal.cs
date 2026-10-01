using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// Represents a structured AI-proposed action that requires host review
/// before being applied to campaign state. Part of the approval queue.
/// </summary>
public class AiActionProposal : EntityBase
{
    public Guid SessionId { get; init; }
    public Guid CampaignId { get; init; }
    public AiProposalStatus Status { get; set; }
    public string ActionType { get; init; }
    public string PayloadJson { get; init; }
    public string? Label { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? RejectionReason { get; set; }

    public AiActionProposal(
        Guid sessionId,
        Guid campaignId,
        string actionType,
        string payloadJson,
        string? label = null,
        string? reason = null)
    {
        SessionId = sessionId;
        CampaignId = campaignId;
        ActionType = actionType;
        PayloadJson = payloadJson;
        Label = label;
        Reason = reason;
        Status = AiProposalStatus.Pending;
    }

    // EF Core constructor
    private AiActionProposal() : base()
    {
        ActionType = string.Empty;
        PayloadJson = "{}";
    }

    /// <summary>
    /// Marks this proposal as approved and records who approved it.
    /// </summary>
    public void Approve(string resolvedBy)
    {
        if (Status != AiProposalStatus.Pending)
            throw new InvalidOperationException($"Cannot approve a proposal with status '{Status}'.");

        Status = AiProposalStatus.Approved;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolvedBy = resolvedBy;
    }

    /// <summary>
    /// Marks this proposal as rejected with an optional reason.
    /// </summary>
    public void Reject(string resolvedBy, string? reason = null)
    {
        if (Status != AiProposalStatus.Pending)
            throw new InvalidOperationException($"Cannot reject a proposal with status '{Status}'.");

        Status = AiProposalStatus.Rejected;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolvedBy = resolvedBy;
        RejectionReason = reason;
    }

    /// <summary>
    /// Marks this proposal as applied (after approval and successful state application).
    /// </summary>
    public void MarkApplied()
    {
        if (Status != AiProposalStatus.Approved)
            throw new InvalidOperationException($"Cannot mark as applied a proposal with status '{Status}'.");

        Status = AiProposalStatus.Applied;
    }
}
