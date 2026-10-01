using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Dice;

/// <summary>
/// Request to record a physical dice roll entered manually by a participant.
/// Formula is optional - physical dice rolls may not have an associated expression.
/// </summary>
public sealed record ManualRollRequest(
    Guid SessionId,
    Guid RollerParticipantId,
    int[] DieResults,
    int Modifier,
    int Total,
    string? Formula = null,
    RollVisibility Visibility = RollVisibility.Public,
    Guid? CharacterId = null,
    string? Context = null);
