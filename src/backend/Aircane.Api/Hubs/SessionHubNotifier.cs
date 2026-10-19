using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Sends server-to-client SignalR events for a session via <see cref="SessionHub"/>.
/// Inject <see cref="ISessionHubNotifier"/> in application/infrastructure services to broadcast events.
/// <para>
/// Each event goes to the narrowest audience that needs it (groups: see <see cref="SessionHub"/>):
/// <list type="bullet">
///   <item><b>Table</b> (approved participants and the host): chat, public rolls, roll requests, AI narration,
///   scene changes with their public text, revealed handouts, combat turns, the adventure's scene, turn and
///   state signals, who joined the turn order, approvals and departures.</item>
///   <item><b>Host only</b>: AI proposals, campaign state (it is the whole state, NPC sheets included), library
///   import status, the adventure's clue, secret and NPC events and its completion notes (DM-facing text;
///   the table hears the story through the narration), and new join requests.</item>
///   <item><b>Host and one participant</b>: a private roll (to its roller) and a denial (to the person denied).</item>
/// </list>
/// </para>
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
        return HostGroup(sessionId).SendAsync("ParticipantJoined", notification, ct);
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

        // A private roll is seen by the person who rolled it and the host, nobody else.
        var audience = roll.Visibility == RollVisibility.Private
            ? _hubContext.Clients.Groups(
                SessionHub.HostGroupName(sessionId),
                SessionHub.ParticipantGroupName(sessionId, roll.RollerParticipantId))
            : SessionGroup(sessionId);
        return audience.SendAsync("RollRecorded", notification, ct);
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
        return HostGroup(sessionId).SendAsync("AIProposalCreated", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyStateUpdatedAsync(Guid sessionId, string stateJson, DateTimeOffset updatedAt, CancellationToken ct = default)
    {
        var notification = new StateUpdatedNotification(sessionId, stateJson, updatedAt);
        return HostGroup(sessionId).SendAsync("StateUpdated", notification, ct);
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
        return HostGroup(sessionId).SendAsync("ImportStatusUpdated", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAdventureSceneAdvancedAsync(Guid sessionId, AdventureSceneAdvancedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("AdventureSceneAdvanced", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAdventureClueRevealedAsync(Guid sessionId, AdventureClueRevealedNotification notification, CancellationToken ct = default)
    {
        return HostGroup(sessionId).SendAsync("AdventureClueRevealed", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAdventureSecretRevealedAsync(Guid sessionId, AdventureSecretRevealedNotification notification, CancellationToken ct = default)
    {
        return HostGroup(sessionId).SendAsync("AdventureSecretRevealed", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAdventureNpcUpdatedAsync(Guid sessionId, AdventureNpcUpdatedNotification notification, CancellationToken ct = default)
    {
        return HostGroup(sessionId).SendAsync("AdventureNpcUpdated", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyAdventureCompletedAsync(Guid sessionId, AdventureCompletedNotification notification, CancellationToken ct = default)
    {
        return HostGroup(sessionId).SendAsync("AdventureCompleted", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyTurnAdvancedAsync(Guid sessionId, TurnAdvancedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("TurnAdvanced", notification, ct);
    }

    /// <inheritdoc />
    public Task NotifyParticipantJoinedAdventureAsync(Guid sessionId, ParticipantJoinedAdventureNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("ParticipantJoinedAdventure", notification, ct);
    }

    public Task NotifyParticipantApprovedAsync(Guid sessionId, ParticipantApprovedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("ParticipantApproved", notification, ct);
    }

    public Task NotifyParticipantDeniedAsync(Guid sessionId, ParticipantDeniedNotification notification, CancellationToken ct = default)
    {
        // The host, and the person denied (who was never at the table, so is only in their own group).
        return _hubContext.Clients.Groups(
                SessionHub.HostGroupName(sessionId),
                SessionHub.ParticipantGroupName(sessionId, notification.ParticipantId))
            .SendAsync("ParticipantDenied", notification, ct);
    }

    public Task NotifyAdventureStateChangedAsync(Guid sessionId, AdventureStateChangedNotification notification, CancellationToken ct = default)
    {
        return SessionGroup(sessionId).SendAsync("AdventureStateChanged", notification, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The table: approved participants and the host.</summary>
    private IClientProxy SessionGroup(Guid sessionId)
        => _hubContext.Clients.Group(SessionHub.SessionGroupName(sessionId));

    /// <summary>The host only.</summary>
    private IClientProxy HostGroup(Guid sessionId)
        => _hubContext.Clients.Group(SessionHub.HostGroupName(sessionId));
}
