using Aircane.Domain.Common;

namespace Aircane.Domain.Entities;

/// <summary>
/// An append-only event in the campaign event log.
/// Used for audit, state restoration, and undo support.
/// ActorType is a free string: "Player", "Host", "AI", "System".
/// </summary>
public class CampaignEvent : EntityBase
{
    public Guid CampaignId { get; init; }
    public Guid? SessionId { get; init; }
    public string ActorType { get; init; }
    public Guid? ActorId { get; init; }
    public string EventType { get; init; }
    public string PayloadJson { get; init; }
    public bool Reversible { get; init; }

    public CampaignEvent(
        Guid campaignId,
        string actorType,
        string eventType,
        string payloadJson,
        bool reversible = false,
        Guid? sessionId = null,
        Guid? actorId = null)
    {
        CampaignId = campaignId;
        ActorType = actorType;
        EventType = eventType;
        PayloadJson = payloadJson;
        Reversible = reversible;
        SessionId = sessionId;
        ActorId = actorId;
    }

    // EF Core constructor
    private CampaignEvent() : base()
    {
        ActorType = string.Empty;
        EventType = string.Empty;
        PayloadJson = "{}";
    }
}
