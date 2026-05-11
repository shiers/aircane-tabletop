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
