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
    /// <returns>A signed JWT string.</returns>
    string IssueToken(Guid sessionId, Guid participantId, string displayName, string role);

    /// <summary>
    /// Validates a participant token and returns the extracted claims if valid.
    /// Returns null if the token is invalid, expired, or the session has been revoked.
    /// </summary>
    /// <param name="token">The raw JWT string.</param>
    /// <returns>The validated claims, or null if validation fails.</returns>
    ParticipantTokenClaims? ValidateToken(string token);
}

/// <summary>
/// Claims extracted from a validated participant token.
/// </summary>
public sealed record ParticipantTokenClaims(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    string Role);
