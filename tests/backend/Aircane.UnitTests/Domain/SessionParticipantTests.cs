using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class SessionParticipantTests
{
    [Fact]
    public void SessionParticipant_Constructor_SetsAllProperties()
    {
        var sessionId = Guid.NewGuid();
        var participant = new SessionParticipant(
            sessionId: sessionId,
            displayName: "Aragorn",
            role: ParticipantRole.Player,
            isApproved: true);

        Assert.Equal(sessionId, participant.SessionId);
        Assert.Equal("Aragorn", participant.DisplayName);
        Assert.Equal(ParticipantRole.Player, participant.Role);
        Assert.True(participant.IsApproved);
        Assert.Null(participant.CharacterId);
    }

    [Fact]
    public void SessionParticipant_DefaultIsApproved_IsFalse()
    {
        var participant = new SessionParticipant(Guid.NewGuid(), "Guest", ParticipantRole.Player);
        Assert.False(participant.IsApproved);
    }

    [Fact]
    public void SessionParticipant_JoinedAt_EqualsCreatedAt()
    {
        var participant = new SessionParticipant(Guid.NewGuid(), "Player", ParticipantRole.Player);
        Assert.Equal(participant.CreatedAt, participant.JoinedAt);
    }

    [Fact]
    public void SessionParticipant_CharacterId_CanBeAssigned()
    {
        var characterId = Guid.NewGuid();
        var participant = new SessionParticipant(Guid.NewGuid(), "Player", ParticipantRole.Player);
        participant.CharacterId = characterId;
        Assert.Equal(characterId, participant.CharacterId);
    }
}
