using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// A participant in a session. Tracks display name, role, assigned character, and approval state.
/// </summary>
public class SessionParticipant : EntityBase
{
    public Guid SessionId { get; init; }
    public string DisplayName { get; init; }
    public ParticipantRole Role { get; set; }
    public Guid? CharacterId { get; set; }
    public DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
    public bool IsApproved { get; set; }

    /// <summary>
    /// The host declined this join request. A denied participant is never approved, takes no part in the
    /// session, and their token is revoked. The record is kept (rather than deleted) so the server can tell
    /// a denied token from an expired one and refuse it with a clear reason.
    /// </summary>
    public bool IsDenied { get; set; }

    /// <summary>
    /// The <c>jti</c> of the participant token last issued to this participant, if any.
    /// Recorded so the host can revoke this exact token when the session ends.
    /// </summary>
    public string? TokenId { get; set; }

    /// <summary>Expiry of the last-issued participant token, used when persisting a revocation.</summary>
    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 (hex) of the rejoin secret the player's browser sent with its join request, or null. The secret
    /// itself is never stored. Holding the secret lets that browser take this seat back after its tab was
    /// closed (a fresh token, no new request). Cleared when the session ends or the seat is replaced; kept on
    /// a denied request only so the browser can be told it was declined (it never mints a token again).
    /// </summary>
    public string? RejoinSecretHash { get; set; }

    /// <summary>
    /// Set when the host gave this participant's seat to a newer join request (the same person on a new
    /// device or tab). A replaced participant has no seat: it is off the roster, its tokens and rejoin
    /// secret are revoked, and its character and turn-order place belong to the participant named here.
    /// </summary>
    public Guid? ReplacedByParticipantId { get; set; }

    /// <summary>
    /// When the host took this participant out of the session, or null. A removed participant is treated like a
    /// denied one: off the roster, no seat, its token revoked, its character and turn-order place given up. The
    /// record is kept (with its rejoin secret) so the server can tell the browser it was removed, rather than
    /// answer as if it had never joined; that secret never mints a token again.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }

    public SessionParticipant(
        Guid sessionId,
        string displayName,
        ParticipantRole role,
        bool isApproved = false,
        Guid? characterId = null)
    {
        SessionId = sessionId;
        DisplayName = displayName;
        Role = role;
        IsApproved = isApproved;
        CharacterId = characterId;
        JoinedAt = CreatedAt;
        LastSeenAt = CreatedAt;
    }

    // EF Core constructor
    private SessionParticipant() : base()
    {
        DisplayName = string.Empty;
        JoinedAt = DateTimeOffset.UtcNow;
        LastSeenAt = DateTimeOffset.UtcNow;
    }
}
