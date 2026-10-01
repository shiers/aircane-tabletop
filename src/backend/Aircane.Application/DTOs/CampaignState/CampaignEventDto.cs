namespace Aircane.Application.DTOs.CampaignState;

/// <summary>
/// Represents a single entry in the campaign event log.
/// </summary>
public sealed record CampaignEventDto(
    Guid Id,
    Guid CampaignId,
    Guid? SessionId,
    string ActorType,
    Guid? ActorId,
    string EventType,
    string PayloadJson,
    bool Reversible,
    DateTimeOffset CreatedAt);
