using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Represents a session and its current state.
/// </summary>
/// <param name="InviteCode">
/// Plain-text invite code. Only populated on session creation; null in all subsequent reads.
/// </param>
/// <param name="JoinUrl">
/// Relative join URL for players. Only populated on session creation; null in all subsequent reads.
/// </param>
public sealed record SessionDto(
    Guid Id,
    Guid CampaignId,
    string Name,
    SessionAccessMode AccessMode,
    SessionStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt,
    int ParticipantCount = 0,
    string? InviteCode = null,
    string? JoinUrl = null);
