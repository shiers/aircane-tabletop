using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class SessionTests
{
    [Fact]
    public void Session_Constructor_SetsAllProperties()
    {
        var campaignId = Guid.NewGuid();
        var session = new Session(
            campaignId: campaignId,
            name: "Session 1",
            accessMode: SessionAccessMode.LocalLan,
            inviteCodeHash: "abc123hash");

        Assert.Equal(campaignId, session.CampaignId);
        Assert.Equal("Session 1", session.Name);
        Assert.Equal(SessionAccessMode.LocalLan, session.AccessMode);
        Assert.Equal("abc123hash", session.InviteCodeHash);
        Assert.Equal(SessionStatus.Pending, session.Status);
        Assert.Null(session.StartedAt);
        Assert.Null(session.EndedAt);
    }

    [Fact]
    public void Session_Status_CanBeUpdated()
    {
        var session = new Session(Guid.NewGuid(), "Test", SessionAccessMode.Solo, "hash");
        session.Status = SessionStatus.Active;
        Assert.Equal(SessionStatus.Active, session.Status);
    }

    [Fact]
    public void Session_StartedAt_CanBeSet()
    {
        var session = new Session(Guid.NewGuid(), "Test", SessionAccessMode.Solo, "hash");
        var now = DateTimeOffset.UtcNow;
        session.StartedAt = now;
        Assert.Equal(now, session.StartedAt);
    }
}
