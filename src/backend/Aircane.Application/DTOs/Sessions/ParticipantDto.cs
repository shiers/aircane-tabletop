using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Represents a participant in a session.
/// <para>
/// <see cref="SameNameAsParticipantId"/> is set on a pending join request whose display name matches
/// (ignoring case) another participant already in the session: probably the same person on a new device or
/// tab. It names that participant, so the host can approve the request as a new player or replace that seat.
/// Only the host and a human DM see it. <see cref="IsReplaced"/> marks a participant whose seat was given to
/// a newer request; such a participant is on nobody's roster.
/// </para>
/// </summary>
public sealed record ParticipantDto(
    Guid Id,
    Guid SessionId,
    string DisplayName,
    ParticipantRole Role,
    Guid? CharacterId,
    bool IsApproved,
    DateTimeOffset JoinedAt,
    DateTimeOffset LastSeenAt,
    bool IsDenied = false,
    Guid? SameNameAsParticipantId = null,
    bool IsReplaced = false);
