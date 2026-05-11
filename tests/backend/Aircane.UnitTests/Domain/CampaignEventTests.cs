using Aircane.Domain.Entities;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class CampaignEventTests
{
    [Fact]
    public void CampaignEvent_Constructor_SetsAllProperties()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var evt = new CampaignEvent(
            campaignId: campaignId,
            actorType: "Player",
            eventType: "CharacterUpdated",
            payloadJson: "{\"hp\":45}",
            reversible: true,
            sessionId: sessionId,
            actorId: actorId);

        Assert.Equal(campaignId, evt.CampaignId);
        Assert.Equal("Player", evt.ActorType);
        Assert.Equal("CharacterUpdated", evt.EventType);
        Assert.Equal("{\"hp\":45}", evt.PayloadJson);
        Assert.True(evt.Reversible);
        Assert.Equal(sessionId, evt.SessionId);
        Assert.Equal(actorId, evt.ActorId);
    }

    [Fact]
    public void CampaignEvent_DefaultReversible_IsFalse()
    {
        var evt = new CampaignEvent(Guid.NewGuid(), "System", "SessionStarted", "{}");
        Assert.False(evt.Reversible);
    }

    [Fact]
    public void CampaignEvent_SessionId_IsOptional()
    {
        var evt = new CampaignEvent(Guid.NewGuid(), "Host", "CampaignCreated", "{}");
        Assert.Null(evt.SessionId);
        Assert.Null(evt.ActorId);
    }

    [Fact]
    public void CampaignEvent_HasUniqueId()
    {
        var a = new CampaignEvent(Guid.NewGuid(), "AI", "NarrateScene", "{}");
        var b = new CampaignEvent(Guid.NewGuid(), "AI", "NarrateScene", "{}");
        Assert.NotEqual(a.Id, b.Id);
    }
}
