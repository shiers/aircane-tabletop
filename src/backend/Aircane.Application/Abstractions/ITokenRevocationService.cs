namespace Aircane.Application.Abstractions;

/// <summary>
/// Tracks revoked participant tokens for session invalidation.
/// MVP uses an in-memory store; a persistent store is required for internet mode.
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>
    /// Revokes all tokens issued for the specified session.
    /// Called when the host ends a session.
    /// </summary>
    void RevokeSession(Guid sessionId);

    /// <summary>
    /// Returns true if the session has been revoked (i.e. the host has ended it).
    /// </summary>
    bool IsSessionRevoked(Guid sessionId);
}
