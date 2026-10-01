using Aircane.Domain.Common;

namespace Aircane.Domain.Entities;

/// <summary>
/// A persistent record that a participant token has been revoked.
/// <para>
/// The in-memory revocation set is a fast-path check that is lost on restart. This table is the
/// authoritative store: token validation consults it (by <see cref="TokenId"/>) so a server
/// restart never re-admits a participant whose token was revoked before the restart.
/// </para>
/// <para>
/// Rows are self-expiring: once <see cref="ExpiresAt"/> passes, the underlying JWT is invalid on
/// its own (lifetime validation), so the row is no longer needed and a cleanup job removes it.
/// </para>
/// </summary>
public class RevokedToken : EntityBase
{
    /// <summary>The revoked token's JWT <c>jti</c> claim (unique token identifier).</summary>
    public string TokenId { get; init; }

    /// <summary>The session the token was issued for.</summary>
    public Guid SessionId { get; init; }

    /// <summary>When the token was revoked.</summary>
    public DateTimeOffset RevokedAt { get; init; }

    /// <summary>
    /// Copy of the token's expiry. Used to bound how long the revocation record must be kept —
    /// after this instant the token is rejected by lifetime validation anyway.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public RevokedToken(string tokenId, Guid sessionId, DateTimeOffset expiresAt)
    {
        TokenId = tokenId;
        SessionId = sessionId;
        RevokedAt = DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
    }

    // EF Core constructor
    private RevokedToken() : base()
    {
        TokenId = string.Empty;
    }
}
