using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.DTOs.Sessions;

namespace Aircane.UnitTests.TestDoubles;

/// <summary>
/// Records the solo-adventure broadcasts (and the group they were sent to); every other
/// notifier method is a no-op.
/// </summary>
internal sealed class RecordingSessionHubNotifier : ISessionHubNotifier
{
    public List<(Guid Group, AdventureSceneAdvancedNotification Event)> SceneAdvanced { get; } = [];
    public List<(Guid Group, AdventureClueRevealedNotification Event)> CluesRevealed { get; } = [];
    public List<(Guid Group, AdventureSecretRevealedNotification Event)> SecretsRevealed { get; } = [];
    public List<(Guid Group, AdventureNpcUpdatedNotification Event)> NpcsUpdated { get; } = [];
    public List<(Guid Group, AdventureCompletedNotification Event)> Completed { get; } = [];

    public Task NotifyAdventureSceneAdvancedAsync(Guid sessionId, AdventureSceneAdvancedNotification notification, CancellationToken ct = default)
    {
        SceneAdvanced.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyAdventureClueRevealedAsync(Guid sessionId, AdventureClueRevealedNotification notification, CancellationToken ct = default)
    {
        CluesRevealed.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyAdventureSecretRevealedAsync(Guid sessionId, AdventureSecretRevealedNotification notification, CancellationToken ct = default)
    {
        SecretsRevealed.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyAdventureNpcUpdatedAsync(Guid sessionId, AdventureNpcUpdatedNotification notification, CancellationToken ct = default)
    {
        NpcsUpdated.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyAdventureCompletedAsync(Guid sessionId, AdventureCompletedNotification notification, CancellationToken ct = default)
    {
        Completed.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public List<(Guid Group, TurnAdvancedNotification Event)> TurnsAdvanced { get; } = [];
    public List<(Guid Group, ParticipantJoinedAdventureNotification Event)> ParticipantsJoinedAdventure { get; } = [];

    public Task NotifyTurnAdvancedAsync(Guid sessionId, TurnAdvancedNotification notification, CancellationToken ct = default)
    {
        TurnsAdvanced.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyParticipantJoinedAdventureAsync(Guid sessionId, ParticipantJoinedAdventureNotification notification, CancellationToken ct = default)
    {
        ParticipantsJoinedAdventure.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public List<(Guid Group, ParticipantDto Participant)> ParticipantsJoined { get; } = [];
    public List<(Guid Group, ParticipantApprovedNotification Event)> ParticipantsApproved { get; } = [];
    public List<(Guid Group, ParticipantDeniedNotification Event)> ParticipantsDenied { get; } = [];
    public List<(Guid Group, AdventureStateChangedNotification Event)> StateChanges { get; } = [];

    public Task NotifyParticipantJoinedAsync(Guid sessionId, ParticipantDto participant, CancellationToken ct = default)
    {
        ParticipantsJoined.Add((sessionId, participant));
        return Task.CompletedTask;
    }

    public Task NotifyParticipantApprovedAsync(Guid sessionId, ParticipantApprovedNotification notification, CancellationToken ct = default)
    {
        ParticipantsApproved.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyParticipantDeniedAsync(Guid sessionId, ParticipantDeniedNotification notification, CancellationToken ct = default)
    {
        ParticipantsDenied.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyAdventureStateChangedAsync(Guid sessionId, AdventureStateChangedNotification notification, CancellationToken ct = default)
    {
        StateChanges.Add((sessionId, notification));
        return Task.CompletedTask;
    }

    public Task NotifyParticipantLeftAsync(Guid sessionId, Guid participantId, string displayName, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyChatMessageReceivedAsync(Guid sessionId, Guid participantId, string displayName, string text, DateTimeOffset sentAt, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyRollRecordedAsync(Guid sessionId, RollDto roll, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyRollRequestedAsync(Guid sessionId, RollRequestedNotification notification, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyAINarrationStartedAsync(Guid sessionId, Guid narrationId, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyAINarrationChunkAsync(Guid sessionId, Guid narrationId, string chunk, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyAINarrationCompletedAsync(Guid sessionId, Guid narrationId, string fullText, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyAIProposalCreatedAsync(Guid sessionId, AIProposalCreatedNotification notification, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyStateUpdatedAsync(Guid sessionId, string stateJson, DateTimeOffset updatedAt, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifySceneChangedAsync(Guid sessionId, SceneChangedNotification notification, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyHandoutRevealedAsync(Guid sessionId, HandoutRevealedNotification notification, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyCombatTurnChangedAsync(Guid sessionId, CombatTurnChangedNotification notification, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyImportStatusUpdatedAsync(Guid sessionId, ImportStatusUpdatedNotification notification, CancellationToken ct = default) => Task.CompletedTask;
}
