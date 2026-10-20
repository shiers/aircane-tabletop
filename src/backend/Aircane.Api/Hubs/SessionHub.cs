using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// SignalR hub for real-time session events.
/// <para>
/// <b>Who the caller is comes from the connection's token, never from a message</b> (<see cref="HubCaller"/>):
/// session, participant, role and display name are the token's claims, and the trusted local host (no
/// token, a direct request from the host machine) is the host. A message naming another session is refused.
/// </para>
/// <para>
/// <b>Groups.</b> Every connection of a participant is in that participant's own group
/// (<see cref="ParticipantGroupName"/>), which is all a participant still waiting for approval is in: they
/// hear their own approval or denial and nothing of the table. Approved participants and the host are also
/// in the table's group (<see cref="SessionGroupName"/>); the host alone is in the host group
/// (<see cref="HostGroupName"/>), which carries what players must not see. When the host approves someone,
/// the server moves their open connections into the table's group (<see cref="SessionHubMembership"/>);
/// when the host denies them, or ends the session, their connections are closed.
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class SessionHub : Hub
{
    private readonly ISessionHostingService _sessions;
    private readonly SessionHubConnections _connections;
    private readonly IChatService _chat;
    private readonly ChatRateLimiter _chatLimiter;
    private readonly ILogger<SessionHub> _logger;

    public SessionHub(
        ISessionHostingService sessions,
        SessionHubConnections connections,
        IChatService chat,
        ChatRateLimiter chatLimiter,
        ILogger<SessionHub> logger)
    {
        _sessions = sessions;
        _connections = connections;
        _chat = chat;
        _chatLimiter = chatLimiter;
        _logger = logger;
    }

    // ── Connecting ────────────────────────────────────────────────────────────

    /// <summary>
    /// A participant token says which session it is for, so its connection is put in the right groups as
    /// soon as it opens. An automatic reconnect is a new connection, so it is back in its groups before the
    /// client has done anything. (The trusted local host has no session in its token and joins one with
    /// <see cref="JoinSession"/>.)
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var caller = HubCaller.From(Context);
        if (caller?.SessionId is { } sessionId)
        {
            try
            {
                await AdmitAsync(caller, sessionId);
            }
            catch (HubException ex)
            {
                // Not a seat at the table (any more): the connection hears nothing; JoinSession says why.
                _logger.LogInformation("Hub connection {ConnectionId} not admitted to session {SessionId}: {Reason}",
                    Context.ConnectionId, sessionId, ex.Message);
            }
        }

        await base.OnConnectedAsync();
    }

    // ── Group management ──────────────────────────────────────────────────────

    /// <summary>
    /// Joins the session's groups as the caller's token allows (see the class summary). Only
    /// <c>message.SessionId</c> is read, and it must be the token's session; the participant id, display
    /// name and role in the message are ignored. Refused (<see cref="HubException"/>) for another session, a
    /// session that has ended, someone not on its roster, and someone the host denied.
    /// </summary>
    public async Task JoinSession(JoinSessionMessage message)
    {
        var caller = RequireCaller();
        if (caller.SessionId is { } tokenSession && tokenSession != message.SessionId)
            throw new HubException("This connection's token is for a different session.");

        await AdmitAsync(caller, message.SessionId);

        _logger.LogInformation(
            "Participant {ParticipantId} ({DisplayName}) joined session {SessionId}",
            caller.ParticipantId, caller.DisplayName, message.SessionId);
    }

    /// <summary>
    /// Leaves the session's groups. Only the session id is read: the participant id and display name
    /// parameters are kept so existing clients still work, but the <c>ParticipantLeft</c> event carries
    /// the token's identity.
    /// </summary>
    public async Task LeaveSession(Guid sessionId, Guid participantId, string displayName)
    {
        var entry = _connections.Get(Context.ConnectionId);
        if (entry is null || entry.SessionId != sessionId)
            return;

        await RemoveFromGroupsAsync(entry);
        _connections.Forget(Context.ConnectionId);
        await AnnounceIfGoneAsync(entry);

        var caller = HubCaller.From(Context);
        _logger.LogInformation(
            "Participant {ParticipantId} ({DisplayName}) left session {SessionId}",
            caller?.ParticipantId, caller?.DisplayName, sessionId);

        // A waiting participant was never at the table, so the table is not told they left.
        if (entry.IsMember && caller?.ParticipantId is { } leaver)
        {
            await Clients.Group(SessionGroupName(sessionId))
                .SendAsync("ParticipantLeft", new ParticipantLeftNotification(sessionId, leaver, caller.DisplayName));
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a chat message to the table. Only <c>message.SessionId</c> and <c>message.Text</c> are read: the
    /// sender is the token's participant and display name. Refused unless the caller is at this session's
    /// table, so a participant still waiting for approval cannot chat. (The host's own messages carry the
    /// participant id the host's client chose, so it can recognise its own; the host has no participant record.)
    /// <para>
    /// A message is stored before it is sent (see <see cref="IChatService"/>), so history and the live feed agree.
    /// It is refused when longer than <c>ChatMessage.MaxTextLength</c> characters, or when the sender is over
    /// <see cref="ChatRateLimiter.MaxMessages"/> messages in <see cref="ChatRateLimiter.Window"/>. Chat is table-wide:
    /// there are no private or host-only messages.
    /// </para>
    /// </summary>
    public async Task SendChatMessage(SendChatMessage message)
    {
        var caller = RequireCaller();
        var entry = _connections.Get(Context.ConnectionId);
        if (entry is null || entry.SessionId != message.SessionId)
            throw new HubException("Join the session before chatting.");

        if (!caller.IsHost)
        {
            // Checked against the roster, not just remembered from the join: approval can only have
            // happened since, and a participant who is no longer approved must not get through.
            var seat = await _sessions.GetParticipantAsync(message.SessionId, caller.ParticipantId!.Value);
            if (seat is null || seat.IsDenied || !seat.IsApproved)
                throw new HubException("You can chat once the host has let you in.");
        }

        if (string.IsNullOrWhiteSpace(message.Text))
            return;
        if (message.Text.Trim().Length > ChatMessage.MaxTextLength)
            throw new HubException($"A chat message can be at most {ChatMessage.MaxTextLength} characters.");

        var senderId = caller.IsHost ? caller.ParticipantId ?? message.ParticipantId : caller.ParticipantId!.Value;
        if (!_chatLimiter.TryAcquire(message.SessionId, senderId))
            throw new HubException("You are sending messages too fast. Wait a few seconds.");

        var stored = await _chat.RecordAsync(message.SessionId, senderId, caller.DisplayName, caller.Role, message.Text);
        if (stored is null)
            return;

        var notification = new ChatMessageReceivedNotification(
            SessionId: message.SessionId,
            ParticipantId: senderId,
            DisplayName: caller.DisplayName,
            Text: stored.Text,
            SentAt: stored.SentAt,
            MessageId: stored.Id,
            Role: caller.Role.ToString());

        await Clients.Group(SessionGroupName(message.SessionId))
            .SendAsync("ChatMessageReceived", notification);
    }

    // ── Admission ─────────────────────────────────────────────────────────────

    private HubCaller RequireCaller()
        => HubCaller.From(Context) ?? throw new HubException("Not signed in to this session.");

    /// <summary>Puts this connection in the groups the caller's seat at the session allows.</summary>
    private async Task AdmitAsync(HubCaller caller, Guid sessionId)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session is null || session.Status == SessionStatus.Ended)
            throw new HubException("This session was not found or has ended.");

        // One session per connection: joining another leaves the first (only the host can do that).
        if (_connections.Get(Context.ConnectionId) is { } previous && previous.SessionId != sessionId)
            await RemoveFromGroupsAsync(previous);

        if (caller.IsHost)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroupName(sessionId));
            await Groups.AddToGroupAsync(Context.ConnectionId, HostGroupName(sessionId));
            if (caller.ParticipantId is { } hostParticipant)
                await Groups.AddToGroupAsync(Context.ConnectionId, ParticipantGroupName(sessionId, hostParticipant));
            _connections.Track(new SessionHubConnections.Entry(
                Context.ConnectionId, Context, sessionId, caller.ParticipantId, IsHost: true, IsMember: true));
            return;
        }

        var participantId = caller.ParticipantId!.Value;
        var participant = await _sessions.GetParticipantAsync(sessionId, participantId);
        if (participant is null || participant.IsReplaced)
            throw new HubException("You are not a participant of this session.");
        if (participant.IsDenied)
            throw new HubException("The host declined your request to join this session.");
        if (participant.IsRemoved)
            throw new HubException("The host removed you from this session.");

        var wasConnected = _connections.IsConnected(sessionId, participantId);

        // Their own group, always: a waiting participant hears their own approval or denial there.
        await Groups.AddToGroupAsync(Context.ConnectionId, ParticipantGroupName(sessionId, participantId));
        if (participant.IsApproved)
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroupName(sessionId));

        _connections.Track(new SessionHubConnections.Entry(
            Context.ConnectionId, Context, sessionId, participantId, IsHost: false, IsMember: participant.IsApproved));

        if (!wasConnected)
            await AnnouncePresenceAsync(sessionId, participantId, isConnected: true);
    }

    /// <summary>
    /// Tells the host (only) that a participant's first connection opened or their last one closed. Presence is
    /// the hub's in-memory connection registry: it is lost on a server restart (everyone reads as away until their
    /// client reconnects), and it never changes the game by itself: an acting player who drops keeps the turn.
    /// </summary>
    private Task AnnouncePresenceAsync(Guid sessionId, Guid participantId, bool isConnected)
        => Clients.Group(HostGroupName(sessionId)).SendAsync(
            "ParticipantPresenceChanged", new ParticipantPresenceChangedNotification(sessionId, participantId, isConnected));

    /// <summary>After a connection was forgotten: tell the host if that was the participant's last one.</summary>
    private async Task AnnounceIfGoneAsync(SessionHubConnections.Entry entry)
    {
        if (entry.IsHost || entry.ParticipantId is not { } participantId)
            return;
        if (!_connections.IsConnected(entry.SessionId, participantId))
            await AnnouncePresenceAsync(entry.SessionId, participantId, isConnected: false);
    }

    private async Task RemoveFromGroupsAsync(SessionHubConnections.Entry entry)
    {
        foreach (var group in GroupsOf(entry))
            await Groups.RemoveFromGroupAsync(entry.ConnectionId, group);
    }

    /// <summary>Every group a tracked connection can be in.</summary>
    internal static IEnumerable<string> GroupsOf(SessionHubConnections.Entry entry)
    {
        yield return SessionGroupName(entry.SessionId);
        if (entry.IsHost)
            yield return HostGroupName(entry.SessionId);
        if (entry.ParticipantId is { } participantId)
            yield return ParticipantGroupName(entry.SessionId, participantId);
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

        // SignalR drops a closed connection from its groups itself; only the bookkeeping is left, and the host
        // hears if that was the participant's last connection. (A connection the server closed itself, on a denial,
        // a removal or the end of the session, was forgotten already: that person is off the roster anyway.)
        if (_connections.Get(Context.ConnectionId) is { } entry)
        {
            _connections.Forget(Context.ConnectionId);
            await AnnounceIfGoneAsync(entry);
        }
        await base.OnDisconnectedAsync(exception);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The table's group: approved participants and the host. Everything the table may see goes here.
    /// </summary>
    public static string SessionGroupName(Guid sessionId) => $"session:{sessionId}";

    /// <summary>The host's group: what players must not see (AI proposals, campaign state, secrets...).</summary>
    public static string HostGroupName(Guid sessionId) => $"session:{sessionId}:host";

    /// <summary>
    /// One participant's own group: every connection they have open, approved or not. A waiting participant
    /// is in this group only; a private roll reaches its roller here.
    /// </summary>
    public static string ParticipantGroupName(Guid sessionId, Guid participantId) => $"session:{sessionId}:participant:{participantId}";
}
