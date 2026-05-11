using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Result returned after a successful session join.
/// Contains the signed participant token and identity details.
/// </summary>
public sealed record JoinSessionResult(
    Guid ParticipantId,
    string DisplayName,
    ParticipantRole Role,
    bool IsApproved,
    string ParticipantToken);
