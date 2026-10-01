namespace Aircane.Application.Abstractions;

/// <summary>
/// Tracks revoked participant tokens for session invalidation.
/// <para>
/// Two layers back this abstraction: a fast in-memory session set (lost on restart) and a
/// persistent per-token store (the <c>RevokedTokens</c> table) that is authoritative across
/// restarts. Callers use the same interface; the implementation keeps both in sync.
/// </para>
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>
    /// Revokes all tokens issued for the specified session in the in-memory fast-path set.
    /// Called when the host ends a session. Persistent revocation of the individual tokens is
    /// done via <see cref="RevokeTokensAsync"/>.
    /// </summary>
    void RevokeSession(Guid sessionId);

    /// <summary>
    /// Returns true if the session has been revoked in the in-memory set (i.e. the host ended
    /// it during this process lifetime).
    /// </summary>
    bool IsSessionRevoked(Guid sessionId);

    /// <summary>
    /// Persists revocations for the given tokens (authoritative across restarts). Called when
    /// the host ends a session, with every active participant token.
    /// </summary>
    Task RevokeTokensAsync(
        IEnumerable<RevokedTokenRecord> tokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the given token id (<c>jti</c>) has a persistent, not-yet-expired
    /// revocation record. Consulted during token validation after the in-memory check.
    /// </summary>
    Task<bool> IsTokenRevokedAsync(
        string tokenId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes persistent revocation records whose expiry has passed. Returns the number of
    /// rows removed. Run on startup and periodically to keep the table bounded.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A single token to persist as revoked.
/// </summary>
public sealed record RevokedTokenRecord(
    string TokenId,
    Guid SessionId,
    DateTimeOffset ExpiresAt);
