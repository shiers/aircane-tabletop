using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// DTO representing an AI action proposal in the approval queue.
/// </summary>
public sealed record AiProposalDto(
    Guid Id,
    Guid SessionId,
    Guid CampaignId,
    string ActionType,
    string PayloadJson,
    string? Label,
    string? Reason,
    AiProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? RejectionReason);
