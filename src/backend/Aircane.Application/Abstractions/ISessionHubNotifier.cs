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

    /// <summary>
    /// Broadcasts that a solo adventure run moved to a new scene.
    /// <paramref name="sessionId"/> is the play session whose group receives the event.
    /// </summary>
    Task NotifyAdventureSceneAdvancedAsync(Guid sessionId, AdventureSceneAdvancedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts that a solo adventure run revealed a clue.</summary>
    Task NotifyAdventureClueRevealedAsync(Guid sessionId, AdventureClueRevealedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts that a solo adventure run revealed a secret.</summary>
    Task NotifyAdventureSecretRevealedAsync(Guid sessionId, AdventureSecretRevealedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts that an NPC's state changed in a solo adventure run.</summary>
    Task NotifyAdventureNpcUpdatedAsync(Guid sessionId, AdventureNpcUpdatedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts that a solo adventure run was completed.</summary>
    Task NotifyAdventureCompletedAsync(Guid sessionId, AdventureCompletedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts that the turn passed to another participant of a group adventure run.</summary>
    Task NotifyTurnAdvancedAsync(Guid sessionId, TurnAdvancedNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Broadcasts that a participant was added to a group adventure run's turn order. The event's
    /// <c>turnPosition</c> is zero-based (0 = first to act).
    /// </summary>
    Task NotifyParticipantJoinedAdventureAsync(Guid sessionId, ParticipantJoinedAdventureNotification notification, CancellationToken ct = default);

    /// <summary>A join request was approved (by the host, or automatically on joining).</summary>
    Task NotifyParticipantApprovedAsync(Guid sessionId, ParticipantApprovedNotification notification, CancellationToken ct = default);

    /// <summary>A join request was denied by the host.</summary>
    Task NotifyParticipantDeniedAsync(Guid sessionId, ParticipantDeniedNotification notification, CancellationToken ct = default);

    /// <summary>An adventure run changed in a way players can see; a signal to re-read it, with no content.</summary>
    Task NotifyAdventureStateChangedAsync(Guid sessionId, AdventureStateChangedNotification notification, CancellationToken ct = default);
}
