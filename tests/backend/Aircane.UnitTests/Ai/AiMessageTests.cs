using Aircane.Application.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="AiMessage"/> factory methods and record equality.
/// </summary>
public class AiMessageTests
{
    [Fact]
    public void System_SetsRoleToSystem()
    {
        var msg = AiMessage.System("You are a DM.");

        Assert.Equal("system", msg.Role);
        Assert.Equal("You are a DM.", msg.Content);
    }

    [Fact]
    public void User_SetsRoleToUser()
    {
        var msg = AiMessage.User("I attack the goblin.");

        Assert.Equal("user", msg.Role);
        Assert.Equal("I attack the goblin.", msg.Content);
    }

    [Fact]
    public void Assistant_SetsRoleToAssistant()
    {
        var msg = AiMessage.Assistant("The goblin dodges.");

        Assert.Equal("assistant", msg.Role);
        Assert.Equal("The goblin dodges.", msg.Content);
    }

    [Fact]
    public void RecordEquality_SameRoleAndContent_AreEqual()
    {
        var a = new AiMessage("user", "Hello");
        var b = new AiMessage("user", "Hello");

        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentRole_AreNotEqual()
    {
        var a = AiMessage.User("Hello");
        var b = AiMessage.Assistant("Hello");

        Assert.NotEqual(a, b);
    }
}
