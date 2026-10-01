using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

public class RollTests
{
    [Fact]
    public void Roll_Constructor_SetsAllProperties()
    {
        var sessionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var roll = new Roll(
            sessionId: sessionId,
            rollerParticipantId: participantId,
            formula: "1d20+5",
            dieResultsJson: "[17]",
            modifier: 5,
            total: 22,
            visibility: RollVisibility.Public,
            isManual: false,
            characterId: characterId,
            context: "Attack roll vs goblin");

        Assert.Equal(sessionId, roll.SessionId);
        Assert.Equal(participantId, roll.RollerParticipantId);
        Assert.Equal("1d20+5", roll.Formula);
        Assert.Equal("[17]", roll.DieResultsJson);
        Assert.Equal(5, roll.Modifier);
        Assert.Equal(22, roll.Total);
        Assert.Equal(RollVisibility.Public, roll.Visibility);
        Assert.False(roll.IsManual);
        Assert.Equal(characterId, roll.CharacterId);
        Assert.Equal("Attack roll vs goblin", roll.Context);
    }

    [Fact]
    public void Roll_ManualRoll_IsManualIsTrue()
    {
        var roll = new Roll(
            sessionId: Guid.NewGuid(),
            rollerParticipantId: Guid.NewGuid(),
            formula: "1d20",
            dieResultsJson: "[14]",
            modifier: 0,
            total: 14,
            isManual: true);

        Assert.True(roll.IsManual);
    }

    [Fact]
    public void Roll_DefaultVisibility_IsPublic()
    {
        var roll = new Roll(Guid.NewGuid(), Guid.NewGuid(), "1d6", "[3]", 0, 3);
        Assert.Equal(RollVisibility.Public, roll.Visibility);
    }

    [Fact]
    public void Roll_PrivateRoll_VisibilityIsPrivate()
    {
        var roll = new Roll(
            sessionId: Guid.NewGuid(),
            rollerParticipantId: Guid.NewGuid(),
            formula: "1d20",
            dieResultsJson: "[8]",
            modifier: 3,
            total: 11,
            visibility: RollVisibility.Private);

        Assert.Equal(RollVisibility.Private, roll.Visibility);
    }
}
