using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Dice;

/// <summary>
/// Represents a recorded roll event.
/// </summary>
/// <param name="RollerName">The roller's display name; null when the roller is not a participant (the host).</param>
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
    DateTimeOffset CreatedAt,
    string? RollerName = null);

/// <summary>
/// Who is reading a roll log, so private rolls are left out in the query itself.
/// </summary>
/// <param name="ParticipantId">The reader's participant id: their own private rolls are theirs to see.</param>
/// <param name="SeesPrivateRolls">The host or a human DM: sees every private roll.</param>
public sealed record RollLogViewer(Guid? ParticipantId, bool SeesPrivateRolls)
{
    /// <summary>The host's view: everything.</summary>
    public static RollLogViewer Everything { get; } = new(null, SeesPrivateRolls: true);
}
