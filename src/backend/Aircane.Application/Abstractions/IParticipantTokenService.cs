namespace Aircane.Application.Abstractions;

/// <summary>
/// Issues and validates signed participant tokens for MVP session authentication.
/// Tokens are JWT-style, HMAC-SHA256 signed, and scoped to a single session.
/// </summary>
public interface IParticipantTokenService
{
    /// <summary>
    /// Issues a signed participant token containing the session identity claims.
    /// </summary>
    /// <param name="sessionId">The session the participant belongs to.</param>
    /// <param name="participantId">The unique participant identifier.</param>
    /// <param name="displayName">The participant's display name.</param>
    /// <param name="role">The participant's role within the session.</param>
    /// <returns>
    /// The signed JWT together with its unique token id (<c>jti</c>) and expiry, so the caller
    /// can persist them for later targeted revocation.
    /// </returns>
    IssuedToken IssueToken(Guid sessionId, Guid participantId, string displayName, string role);

    /// <summary>
    /// Validates a participant token and returns the extracted claims if valid.
    /// Returns null if the token is invalid, expired, or the session has been revoked.
    /// </summary>
    /// <param name="token">The raw JWT string.</param>
    /// <returns>The validated claims, or null if validation fails.</returns>
    /// <remarks>
    /// This synchronous overload checks signature, lifetime, and the in-memory session
    /// revocation fast path only. Prefer <see cref="ValidateTokenAsync"/>, which additionally
    /// consults the persistent per-token revocation store.
    /// </remarks>
    ParticipantTokenClaims? ValidateToken(string token);

    /// <summary>
    /// Validates a participant token and returns the extracted claims if valid, additionally
    /// checking the persistent per-token revocation store (authoritative across restarts).
    /// Returns null if the token is invalid, expired, or revoked.
    /// </summary>
    Task<ParticipantTokenClaims?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A freshly issued participant token and the metadata needed to revoke it later.
/// </summary>
public sealed record IssuedToken(
    string Token,
    string TokenId,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Claims extracted from a validated participant token.
/// </summary>
public sealed record ParticipantTokenClaims(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    string Role,
    string TokenId);
