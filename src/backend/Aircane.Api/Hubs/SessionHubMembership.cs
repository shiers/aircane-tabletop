using Aircane.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Puts the host's decisions into effect on open hub connections (see <see cref="ISessionHubMembership"/>).
/// SignalR groups are keyed by connection id, so the connections a participant has open are looked up in
/// <see cref="SessionHubConnections"/>, which the hub fills as connections join.
/// </summary>
public sealed class SessionHubMembership : ISessionHubMembership
{
    private readonly IHubContext<SessionHub> _hub;
    private readonly SessionHubConnections _connections;
    private readonly ILogger<SessionHubMembership> _logger;

    public SessionHubMembership(IHubContext<SessionHub> hub, SessionHubConnections connections, ILogger<SessionHubMembership> logger)
    {
        _hub = hub;
        _connections = connections;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AdmitParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default)
    {
        // The move: each connection the participant has open (all of them already in their own group) is
        // added to the table's group and marked as a member. A connection that opens later is admitted by
        // the hub itself, from the roster.
        foreach (var entry in _connections.ForParticipant(sessionId, participantId))
        {
            await _hub.Groups.AddToGroupAsync(entry.ConnectionId, SessionHub.SessionGroupName(sessionId), ct);
            _connections.MarkMember(entry.ConnectionId);
        }
    }

    /// <inheritdoc />
    public async Task RemoveParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default)
    {
        var entries = _connections.ForParticipant(sessionId, participantId);
        await CloseAsync(entries, ct);
        if (entries.Count > 0)
            _logger.LogInformation("Closed {Count} hub connection(s) of participant {ParticipantId} in session {SessionId}",
                entries.Count, participantId, sessionId);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, bool> Presence(Guid sessionId, IEnumerable<Guid> participantIds)
        => participantIds.Distinct().ToDictionary(id => id, id => _connections.IsConnected(sessionId, id));

    /// <inheritdoc />
    public async Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var entries = _connections.ForSession(sessionId);
        await CloseAsync(entries, ct);
        if (entries.Count > 0)
            _logger.LogInformation("Closed {Count} hub connection(s) of ended session {SessionId}", entries.Count, sessionId);
    }

    /// <summary>
    /// Out of every group first, so nothing more reaches the connection even before it has closed, then
    /// closed. The participant's token has been revoked (denial) or the session revoked (end), so a client
    /// that tries to reconnect is refused.
    /// </summary>
    private async Task CloseAsync(IReadOnlyList<SessionHubConnections.Entry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            foreach (var group in SessionHub.GroupsOf(entry))
                await _hub.Groups.RemoveFromGroupAsync(entry.ConnectionId, group, ct);
            _connections.Forget(entry.ConnectionId);
            entry.Context.Abort();
        }
    }
}
