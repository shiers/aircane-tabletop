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
    /// The host denied a participant: their open connections leave every group of the session and are closed.
    /// </summary>
    Task RemoveParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default);

    /// <summary>The host ended the session: every connection to it is closed.</summary>
    Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default);
}
