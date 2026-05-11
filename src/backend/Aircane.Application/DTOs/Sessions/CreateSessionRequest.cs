using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Request to create a new session under a campaign.
/// </summary>
public sealed record CreateSessionRequest(
    Guid CampaignId,
    string Name,
    SessionAccessMode AccessMode,
    bool RequireHostApproval = true);
