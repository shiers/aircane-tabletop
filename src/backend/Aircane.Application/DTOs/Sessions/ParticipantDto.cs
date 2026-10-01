using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Represents a participant in a session.
/// </summary>
public sealed record ParticipantDto(
    Guid Id,
    Guid SessionId,
    string DisplayName,
    ParticipantRole Role,
    Guid? CharacterId,
    bool IsApproved,
    DateTimeOffset JoinedAt,
    DateTimeOffset LastSeenAt);
