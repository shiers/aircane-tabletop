using Aircane.Api.Hubs;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Aircane.UnitTests.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionHub"/> static helpers and <see cref="SessionHubNotifier"/>.
/// </summary>
public class SessionHubGroupNameTests
{
    [Fact]
    public void SessionGroupName_IncludesSessionId()
    {
        var id = Guid.NewGuid();
        var name = SessionHub.SessionGroupName(id);

        Assert.Contains(id.ToString(), name);
    }

    [Fact]
    public void SessionGroupName_DifferentIds_ProduceDifferentNames()
    {
        var name1 = SessionHub.SessionGroupName(Guid.NewGuid());
        var name2 = SessionHub.SessionGroupName(Guid.NewGuid());

        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void SessionGroupName_SameId_ProducesSameName()
    {
        var id = Guid.NewGuid();

        Assert.Equal(SessionHub.SessionGroupName(id), SessionHub.SessionGroupName(id));
    }

    [Fact]
    public void SessionGroupName_HasExpectedPrefix()
    {
        var id = Guid.NewGuid();
        var name = SessionHub.SessionGroupName(id);

        Assert.StartsWith("session:", name);
    }
}

/// <summary>
/// Unit tests for <see cref="SessionHubNotifier"/> using a hand-rolled fake hub context.
/// Verifies that each notifier method targets the correct SignalR group and uses the correct event name.
/// </summary>
public class SessionHubNotifierTests
{
    private readonly FakeHubContext _fakeContext;
    private readonly SessionHubNotifier _notifier;

    public SessionHubNotifierTests()
    {
        _fakeContext = new FakeHubContext();
        _notifier = new SessionHubNotifier(_fakeContext);
    }

    private static Guid NewSession() => Guid.NewGuid();

    // ── ParticipantJoined ─────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyParticipantJoinedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var participant = MakeParticipant(sessionId);

        await _notifier.NotifyParticipantJoinedAsync(sessionId, participant);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("ParticipantJoined", _fakeContext.LastMethod);
    }

    // ── ParticipantLeft ───────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyParticipantLeftAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();

        await _notifier.NotifyParticipantLeftAsync(sessionId, Guid.NewGuid(), "Bob");

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("ParticipantLeft", _fakeContext.LastMethod);
    }

    // ── ChatMessageReceived ───────────────────────────────────────────────────

    [Fact]
    public async Task NotifyChatMessageReceivedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();

        await _notifier.NotifyChatMessageReceivedAsync(
            sessionId, Guid.NewGuid(), "Alice", "Hello!", DateTimeOffset.UtcNow);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("ChatMessageReceived", _fakeContext.LastMethod);
    }

    // ── RollRecorded ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyRollRecordedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var roll = MakeRoll(sessionId);

        await _notifier.NotifyRollRecordedAsync(sessionId, roll);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("RollRecorded", _fakeContext.LastMethod);
    }

    // ── RollRequested ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyRollRequestedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var notification = new RollRequestedNotification(
            sessionId, Guid.NewGuid(), null, "1d20+3", "Perception Check", 15, "Spot the door");

        await _notifier.NotifyRollRequestedAsync(sessionId, notification);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("RollRequested", _fakeContext.LastMethod);
    }

    // ── AI Narration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyAINarrationStartedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();

        await _notifier.NotifyAINarrationStartedAsync(sessionId, Guid.NewGuid());

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("AINarrationStarted", _fakeContext.LastMethod);
    }

    [Fact]
    public async Task NotifyAINarrationChunkAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();

        await _notifier.NotifyAINarrationChunkAsync(sessionId, Guid.NewGuid(), "The dragon roars...");

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("AINarrationChunk", _fakeContext.LastMethod);
    }

    [Fact]
    public async Task NotifyAINarrationCompletedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();

        await _notifier.NotifyAINarrationCompletedAsync(sessionId, Guid.NewGuid(), "The dragon attacks!");

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("AINarrationCompleted", _fakeContext.LastMethod);
    }

    // ── AIProposalCreated ─────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyAIProposalCreatedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var notification = new AIProposalCreatedNotification(
            sessionId, Guid.NewGuid(), "RequestRoll", "Roll Perception", "{}");

        await _notifier.NotifyAIProposalCreatedAsync(sessionId, notification);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("AIProposalCreated", _fakeContext.LastMethod);
    }

    // ── StateUpdated ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyStateUpdatedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();

        await _notifier.NotifyStateUpdatedAsync(sessionId, "{}", DateTimeOffset.UtcNow);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("StateUpdated", _fakeContext.LastMethod);
    }

    // ── SceneChanged ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifySceneChangedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var notification = new SceneChangedNotification(
            sessionId, Guid.NewGuid(), "The Tavern", "A dimly lit tavern.");

        await _notifier.NotifySceneChangedAsync(sessionId, notification);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("SceneChanged", _fakeContext.LastMethod);
    }

    // ── HandoutRevealed ───────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyHandoutRevealedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var notification = new HandoutRevealedNotification(
            sessionId, Guid.NewGuid(), "Handout", "Ancient Map", "A faded map.");

        await _notifier.NotifyHandoutRevealedAsync(sessionId, notification);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("HandoutRevealed", _fakeContext.LastMethod);
    }

    // ── CombatTurnChanged ─────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyCombatTurnChangedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var notification = new CombatTurnChangedNotification(
            sessionId, Guid.NewGuid(), "Goblin", 1, 0);

        await _notifier.NotifyCombatTurnChangedAsync(sessionId, notification);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("CombatTurnChanged", _fakeContext.LastMethod);
    }

    // ── ImportStatusUpdated ───────────────────────────────────────────────────

    [Fact]
    public async Task NotifyImportStatusUpdatedAsync_UsesCorrectGroupAndEvent()
    {
        var sessionId = NewSession();
        var notification = new ImportStatusUpdatedNotification(
            sessionId, Guid.NewGuid(), "Completed", 100, null);

        await _notifier.NotifyImportStatusUpdatedAsync(sessionId, notification);

        Assert.Equal(SessionHub.SessionGroupName(sessionId), _fakeContext.LastGroupName);
        Assert.Equal("ImportStatusUpdated", _fakeContext.LastMethod);
    }

    // ── Group isolation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Notifier_DifferentSessions_TargetDifferentGroups()
    {
        var sessionA = NewSession();
        var sessionB = NewSession();

        await _notifier.NotifyStateUpdatedAsync(sessionA, "{}", DateTimeOffset.UtcNow);
        var groupA = _fakeContext.LastGroupName;

        await _notifier.NotifyStateUpdatedAsync(sessionB, "{}", DateTimeOffset.UtcNow);
        var groupB = _fakeContext.LastGroupName;

        Assert.NotEqual(groupA, groupB);
        Assert.Equal(SessionHub.SessionGroupName(sessionA), groupA);
        Assert.Equal(SessionHub.SessionGroupName(sessionB), groupB);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ParticipantDto MakeParticipant(Guid sessionId) =>
        new(Guid.NewGuid(), sessionId, "Alice", ParticipantRole.Player,
            null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static RollDto MakeRoll(Guid sessionId) =>
        new(Guid.NewGuid(), sessionId, null, Guid.NewGuid(),
            "1d20+5", [15], 5, 20, false, RollVisibility.Public, null, DateTimeOffset.UtcNow);
}

// ── Fakes ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal fake <see cref="IHubContext{THub}"/> that records the last group name and method name
/// sent via <see cref="IClientProxy.SendCoreAsync"/>.
/// </summary>
internal sealed class FakeHubContext : IHubContext<SessionHub>
{
    public string? LastGroupName { get; private set; }
    public string? LastMethod { get; private set; }

    public IHubClients Clients => new FakeHubClients(this);
    public IGroupManager Groups => throw new NotImplementedException();

    internal void Record(string groupName, string method)
    {
        LastGroupName = groupName;
        LastMethod = method;
    }
}

internal sealed class FakeHubClients : IHubClients
{
    private readonly FakeHubContext _ctx;

    public FakeHubClients(FakeHubContext ctx) => _ctx = ctx;

    public IClientProxy All => new FakeClientProxy(_ctx, "__all__");
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
    public IClientProxy Client(string connectionId) => throw new NotImplementedException();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
    public IClientProxy Group(string groupName) => new FakeClientProxy(_ctx, groupName);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
    public IClientProxy User(string userId) => throw new NotImplementedException();
    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
}

internal sealed class FakeClientProxy : IClientProxy
{
    private readonly FakeHubContext _ctx;
    private readonly string _groupName;

    public FakeClientProxy(FakeHubContext ctx, string groupName)
    {
        _ctx = ctx;
        _groupName = groupName;
    }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        _ctx.Record(_groupName, method);
        return Task.CompletedTask;
    }
}
