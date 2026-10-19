using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Which session each open hub connection belongs to, as whom, and whether it is in the table's group.
/// <para>
/// SignalR keeps group membership but cannot say which connections belong to a participant. The server
/// needs exactly that to act on its own: move a waiting participant into the table's group when the host
/// approves them, and close a participant's (or a whole session's) connections when the host denies them
/// or ends the session. One process, so this lives in memory; it is rebuilt as clients reconnect.
/// </para>
/// </summary>
public sealed class SessionHubConnections
{
    /// <summary>One open connection.</summary>
    /// <param name="IsMember">In the table's group (an approved participant, or the host).</param>
    public sealed record Entry(
        string ConnectionId,
        HubCallerContext Context,
        Guid SessionId,
        Guid? ParticipantId,
        bool IsHost,
        bool IsMember);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void Track(Entry entry) => _entries[entry.ConnectionId] = entry;

    public Entry? Get(string connectionId) => _entries.GetValueOrDefault(connectionId);

    public void Forget(string connectionId) => _entries.TryRemove(connectionId, out _);

    /// <summary>Marks a connection as being in the table's group.</summary>
    public void MarkMember(string connectionId)
    {
        if (_entries.TryGetValue(connectionId, out var entry))
            _entries[connectionId] = entry with { IsMember = true };
    }

    public IReadOnlyList<Entry> ForParticipant(Guid sessionId, Guid participantId)
        => _entries.Values.Where(e => e.SessionId == sessionId && e.ParticipantId == participantId).ToList();

    public IReadOnlyList<Entry> ForSession(Guid sessionId)
        => _entries.Values.Where(e => e.SessionId == sessionId).ToList();
}
