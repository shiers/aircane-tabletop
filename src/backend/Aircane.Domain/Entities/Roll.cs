using Aircane.Domain.Common;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// Records a single dice roll event within a session.
/// Stores the formula, individual die results, modifier, total, and visibility.
/// </summary>
public class Roll : EntityBase
{
    public Guid SessionId { get; init; }
    public Guid? CharacterId { get; init; }
    public Guid RollerParticipantId { get; init; }
    /// <summary>Dice expression formula. Null for manual physical dice rolls.</summary>
    public string? Formula { get; init; }

    /// <summary>JSON array of individual die results, e.g. [4, 17].</summary>
    public string DieResultsJson { get; init; }

    public int Modifier { get; init; }
    public int Total { get; init; }
    public bool IsManual { get; init; }
    public RollVisibility Visibility { get; init; }
    public string? Context { get; init; }

    public Roll(
        Guid sessionId,
        Guid rollerParticipantId,
        string? formula,
        string dieResultsJson,
        int modifier,
        int total,
        RollVisibility visibility = RollVisibility.Public,
        bool isManual = false,
        Guid? characterId = null,
        string? context = null)
    {
        SessionId = sessionId;
        RollerParticipantId = rollerParticipantId;
        Formula = formula;
        DieResultsJson = dieResultsJson;
        Modifier = modifier;
        Total = total;
        Visibility = visibility;
        IsManual = isManual;
        CharacterId = characterId;
        Context = context;
    }

    // EF Core constructor
    private Roll() : base()
    {
        Formula = null;
        DieResultsJson = "[]";
    }
}
