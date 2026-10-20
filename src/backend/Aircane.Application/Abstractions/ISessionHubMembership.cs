namespace Aircane.Application.Abstractions;

/// <summary>
/// Changes to who is connected to a session's live channel that the server makes itself, as the host's
/// decisions take effect. Implemented next to the SignalR hub, which keeps track of the open connections.
/// </summary>
public interface ISessionHubMembership
{
    /// <summary>
    /// The host approved a participant: their open connections join the table's group, so they start
    /// hearing the table (and their own <c>ParticipantApproved</c>) without reconnecting.
    /// </summary>
    Task AdmitParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default);

    /// <summary>
    /// The host denied or removed a participant: their open connections leave every group of the session and are
    /// closed.
    /// </summary>
    Task RemoveParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default);

    /// <summary>The host ended the session: every connection to it is closed.</summary>
    Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Whether each of these participants has a hub connection to the session open right now. Kept in memory by
    /// the hub, so it starts empty (everyone away) after a server restart until clients reconnect.
    /// </summary>
    IReadOnlyDictionary<Guid, bool> Presence(Guid sessionId, IEnumerable<Guid> participantIds);
}
