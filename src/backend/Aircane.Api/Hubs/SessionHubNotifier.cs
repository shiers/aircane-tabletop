using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.DTOs.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Sends server-to-client SignalR events to the session-scoped group via <see cref="SessionHub"/>.
/// Inject <see cref="ISessionHubNotifier"/> in application/infrastructure services to broadcast events.
/// </summary>
public sealed class SessionHubNotifier : ISessionHubNotifier
{
    private readonly IHubContext<SessionHub> _hubContext;

    public SessionHubNotifier(IHubContext<SessionHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc />
    public Task NotifyParticipantJoinedAsync(Guid sessionId, ParticipantDto participant, CancellationToken ct = default)
    {
        var notification = new ParticipantJoinedNotification(sessionId, participant);
        return SessionGroup(sessionId).SendAsync("ParticipantJoined", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyParticipantLeftAsync(Guid sessionId, Guid participantId, string displayName, CancellationToken ct = default)
    {
        var notification = new ParticipantLeftNotification(sessionId, participantId, displayName);
        return SessionGroup(sessionId).SendAsync("ParticipantLeft", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyChatMessageReceivedAsync(Guid sessionId, Guid participantId, string displayName, string text, DateTimeOffset sentAt, CancellationToken ct = default)
    {
        var notification = new ChatMessageReceivedNotification(sessionId, participantId, displayName, text, sentAt);
        return SessionGroup(sessionId).SendAsync("ChatMessageReceived", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyRollRecordedAsync(Guid sessionId, RollDto roll, CancellationToken ct = default)
    {
        var notification = new RollRecordedNotification(sessionId, roll);
        return SessionGroup(sessionId).SendAsync("RollRecorded", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyRollRequestedAsync(Guid sessionId, RollRequestedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("RollRequested", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAINarrationStartedAsync(Guid sessionId, Guid narrationId, CancellationToken ct = default)
    {
        var notification = new AINarrationStartedNotification(sessionId, narrationId);
        return SessionGroup(sessionId).SendAsync("AINarrationStarted", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAINarrationChunkAsync(Guid sessionId, Guid narrationId, string chunk, CancellationToken ct = default)
    {
        var notification = new AINarrationChunkNotification(sessionId, narrationId, chunk);
        return SessionGroup(sessionId).SendAsync("AINarrationChunk", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAINarrationCompletedAsync(Guid sessionId, Guid narrationId, string fullText, CancellationToken ct = default)
    {
        var notification = new AINarrationCompletedNotification(sessionId, narrationId, fullText);
        return SessionGroup(sessionId).SendAsync("AINarrationCompleted", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAIProposalCreatedAsync(Guid sessionId, AIProposalCreatedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("AIProposalCreated", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyStateUpdatedAsync(Guid sessionId, string stateJson, DateTimeOffset updatedAt, CancellationToken ct = default)
    {
        var notification = new StateUpdatedNotification(sessionId, stateJson, updatedAt);
        return SessionGroup(sessionId).SendAsync("StateUpdated", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifySceneChangedAsync(Guid sessionId, SceneChangedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("SceneChanged", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyHandoutRevealedAsync(Guid sessionId, HandoutRevealedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("HandoutRevealed", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyCombatTurnChangedAsync(Guid sessionId, CombatTurnChangedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("CombatTurnChanged", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyImportStatusUpdatedAsync(Guid sessionId, ImportStatusUpdatedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("ImportStatusUpdated", notification, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IClientProxy SessionGroup(Guid sessionId)
        => _hubContext.Clients.Group(SessionHub.SessionGroupName(sessionId));
}
