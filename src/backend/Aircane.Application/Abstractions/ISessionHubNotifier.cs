using Aircane.Application.DTOs.Sessions;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Sends real-time session events to connected clients via SignalR.
/// All methods target the group for the given <paramref name="sessionId"/> unless noted.
/// </summary>
public interface ISessionHubNotifier
{
    /// <summary>Notifies all session participants that a new participant has joined.</summary>
    Task NotifyParticipantJoinedAsync(Guid sessionId, ParticipantDto participant, CancellationToken ct = default);

    /// <summary>Notifies all session participants that a participant has left.</summary>
    Task NotifyParticipantLeftAsync(Guid sessionId, Guid participantId, string displayName, CancellationToken ct = default);

    /// <summary>Broadcasts a chat message to all session participants.</summary>
    Task NotifyChatMessageReceivedAsync(Guid sessionId, Guid participantId, string displayName, string text, DateTimeOffset sentAt, CancellationToken ct = default);

    /// <summary>
    /// Broadcasts a recorded roll to the session.
    /// Public rolls go to all participants; private rolls should be sent only to the roller and host
    /// by the caller before invoking this method.
    /// </summary>
    Task NotifyRollRecordedAsync(Guid sessionId, DTOs.Dice.RollDto roll, CancellationToken ct = default);

    /// <summary>Sends a roll request to a specific participant (and the host).</summary>
    Task NotifyRollRequestedAsync(Guid sessionId, RollRequestedNotification notification, CancellationToken ct = default);

    /// <summary>Notifies the session that AI narration has started streaming.</summary>
    Task NotifyAINarrationStartedAsync(Guid sessionId, Guid narrationId, CancellationToken ct = default);

    /// <summary>Sends a streamed chunk of AI narration text to the session.</summary>
    Task NotifyAINarrationChunkAsync(Guid sessionId, Guid narrationId, string chunk, CancellationToken ct = default);

    /// <summary>Notifies the session that AI narration streaming is complete.</summary>
    Task NotifyAINarrationCompletedAsync(Guid sessionId, Guid narrationId, string fullText, CancellationToken ct = default);

    /// <summary>Notifies the host that a new AI proposal is awaiting approval.</summary>
    Task NotifyAIProposalCreatedAsync(Guid sessionId, AIProposalCreatedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts a campaign state update to all session participants.</summary>
    Task NotifyStateUpdatedAsync(Guid sessionId, string stateJson, DateTimeOffset updatedAt, CancellationToken ct = default);

    /// <summary>Broadcasts a scene change to all session participants.</summary>
    Task NotifySceneChangedAsync(Guid sessionId, SceneChangedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts a revealed handout or hidden content to all session participants.</summary>
    Task NotifyHandoutRevealedAsync(Guid sessionId, HandoutRevealedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts a combat turn change to all session participants.</summary>
    Task NotifyCombatTurnChangedAsync(Guid sessionId, CombatTurnChangedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts a document import status update to the host.</summary>
    Task NotifyImportStatusUpdatedAsync(Guid sessionId, ImportStatusUpdatedNotification notification, CancellationToken ct = default);
}
