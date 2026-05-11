using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Dice;

/// <summary>
/// Request to roll a dice expression within a session.
/// </summary>
public sealed record RollRequest(
    Guid SessionId,
    Guid RollerParticipantId,
    string Formula,
    RollVisibility Visibility = RollVisibility.Public,
    Guid? CharacterId = null,
    string? Context = null);
