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
