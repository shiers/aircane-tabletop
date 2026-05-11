namespace Aircane.Application.DTOs.CampaignState;

/// <summary>
/// Request to append a raw event to the campaign event log.
/// </summary>
public sealed record AppendEventRequest(
    Guid CampaignId,
    string EventType,
    string PayloadJson,
    string ActorType,
    bool Reversible = false,
    Guid? ActorId = null,
    Guid? SessionId = null);
