using Aircane.Application.Abstractions;

namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// Request to create a new AI action proposal in the approval queue.
/// </summary>
public sealed record CreateProposalRequest(
    Guid SessionId,
    Guid CampaignId,
    AiProposedAction ProposedAction);
