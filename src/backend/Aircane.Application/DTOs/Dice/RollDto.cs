using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Dice;

/// <summary>
/// Represents a recorded roll event.
/// </summary>
public sealed record RollDto(
    Guid Id,
    Guid SessionId,
    Guid? CharacterId,
    Guid RollerParticipantId,
    string? Formula,
    int[] DieResults,
    int Modifier,
    int Total,
    bool IsManual,
    RollVisibility Visibility,
    string? Context,
    DateTimeOffset CreatedAt);
