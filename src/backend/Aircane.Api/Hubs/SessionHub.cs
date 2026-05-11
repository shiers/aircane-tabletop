using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// SignalR hub for real-time session events.
/// Clients join a session-scoped group identified by the session ID so that
/// messages are delivered only to participants of the correct session.
/// Requires a valid participant token (any authenticated role).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class SessionHub : Hub
{
    private readonly ISessionHostingService _sessions;
    private readonly ILogger<SessionHub> _logger;

    public SessionHub(ISessionHostingService sessions, ILogger<SessionHub> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    // ── Group management ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by a client to join a session group and announce their presence.
    /// Adds the connection to the session-scoped SignalR group and broadcasts
    /// a <c>ParticipantJoined</c> event to all other participants.
    /// </summary>
    public async Task JoinSession(JoinSessionMessage message)
    {
        var groupName = SessionGroupName(message.SessionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Participant {ParticipantId} ({DisplayName}) joined session {SessionId}",
            message.ParticipantId, message.DisplayName, message.SessionId);

        var notification = new ParticipantJoinedNotification(
            SessionId: message.SessionId,
            Participant: new ParticipantDto(
                Id: message.ParticipantId,
                SessionId: message.SessionId,
                DisplayName: message.DisplayName,
                Role: message.Role,
                CharacterId: null,
                IsApproved: true,
                JoinedAt: DateTimeOffset.UtcNow,
                LastSeenAt: DateTimeOffset.UtcNow));

        await Clients.OthersInGroup(groupName)
            .SendAsync("ParticipantJoined", notification);
    }

    /// <summary>
    /// Called by a client to leave a session group.
    /// Removes the connection from the session-scoped group and broadcasts
    /// a <c>ParticipantLeft</c> event to remaining participants.
    /// </summary>
    public async Task LeaveSession(Guid sessionId, Guid participantId, string displayName)
    {
        var groupName = SessionGroupName(sessionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Participant {ParticipantId} ({DisplayName}) left session {SessionId}",
            participantId, displayName, sessionId);

        var notification = new ParticipantLeftNotification(
            SessionId: sessionId,
            ParticipantId: participantId,
            DisplayName: displayName);

        await Clients.Group(groupName)
            .SendAsync("ParticipantLeft", notification);
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcasts a chat message to all participants in the session.
    /// </summary>
    public async Task SendChatMessage(SendChatMessage message)
    {
        var groupName = SessionGroupName(message.SessionId);

        var notification = new ChatMessageReceivedNotification(
            SessionId: message.SessionId,
            ParticipantId: message.ParticipantId,
            DisplayName: message.DisplayName,
            Text: message.Text,
            SentAt: DateTimeOffset.UtcNow);

        await Clients.Group(groupName)
            .SendAsync("ChatMessageReceived", notification);
    }

    // ── Dice ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acknowledges a dice roll request from a client.
    /// The actual roll is processed by the REST API; this method exists so clients
    /// can trigger a roll via the hub connection if preferred.
    /// The server broadcasts <c>RollRecorded</c> via <see cref="SessionHubNotifier"/>
    /// after the roll is persisted.
    /// </summary>
    public Task RollDice(RollDiceMessage message)
    {
        // Roll processing is handled by the REST API (POST /api/sessions/{id}/rolls).
        // This hub method is a convenience entry point; the caller should also call
        // the REST endpoint to persist the roll and receive the broadcast.
        _logger.LogDebug(
            "RollDice hub message received from participant {ParticipantId} in session {SessionId}: {Formula}",
            message.ParticipantId, message.SessionId, message.Formula);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Acknowledges a manual roll entry from a client.
    /// The actual persistence is handled by the REST API.
    /// </summary>
    public Task EnterManualRoll(EnterManualRollMessage message)
    {
        _logger.LogDebug(
            "EnterManualRoll hub message received from participant {ParticipantId} in session {SessionId}: value={Value}",
            message.ParticipantId, message.SessionId, message.Value);

        return Task.CompletedTask;
    }

    // ── AI ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acknowledges a player action submission.
    /// Full processing is handled by the REST API (POST /api/sessions/{id}/ai/player-action).
    /// </summary>
    public Task SubmitPlayerAction(SubmitPlayerActionMessage message)
    {
        _logger.LogDebug(
            "SubmitPlayerAction hub message received from participant {ParticipantId} in session {SessionId}",
            message.ParticipantId, message.SessionId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Acknowledges a rules lookup request.
    /// Full processing is handled by the REST API (POST /api/ai/rules-question).
    /// </summary>
    public Task RequestRulesLookup(RequestRulesLookupMessage message)
    {
        _logger.LogDebug(
            "RequestRulesLookup hub message received from participant {ParticipantId} in session {SessionId}",
            message.ParticipantId, message.SessionId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Acknowledges an AI proposal approval.
    /// Full processing is handled by the REST API (POST /api/sessions/{id}/ai/proposals/{proposalId}/approve).
    /// </summary>
    public Task ApproveAIProposal(ApproveAIProposalMessage message)
    {
        _logger.LogDebug(
            "ApproveAIProposal hub message received from participant {ParticipantId} in session {SessionId}, proposal {ProposalId}",
            message.ParticipantId, message.SessionId, message.ProposalId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Acknowledges an AI proposal rejection.
    /// Full processing is handled by the REST API (POST /api/sessions/{id}/ai/proposals/{proposalId}/reject).
    /// </summary>
    public Task RejectAIProposal(RejectAIProposalMessage message)
    {
        _logger.LogDebug(
            "RejectAIProposal hub message received from participant {ParticipantId} in session {SessionId}, proposal {ProposalId}",
            message.ParticipantId, message.SessionId, message.ProposalId);

        return Task.CompletedTask;
    }

    // ── Disconnect ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            _logger.LogWarning(exception,
                "Client {ConnectionId} disconnected with error", Context.ConnectionId);
        }
        else
        {
            _logger.LogDebug("Client {ConnectionId} disconnected", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the SignalR group name for a session.
    /// All participants of the same session share this group.
    /// </summary>
    public static string SessionGroupName(Guid sessionId) => $"session:{sessionId}";
}
