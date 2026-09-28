namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// API-facing snapshot of a live combat encounter. Mirrors the domain
/// <see cref="Aircane.Domain.Combat.EncounterState"/> in a client-friendly shape.
/// </summary>
public sealed record EncounterStateDto(
    string EncounterId,
    bool IsActive,
    int Round,
    int TurnIndex,
    string? ActiveCombatantId,
    IReadOnlyList<string> InitiativeOrder,
    IReadOnlyList<CombatantDto> Combatants);

/// <summary>API-facing view of a single combatant.</summary>
public sealed record CombatantDto(
    string Id,
    string Name,
    bool IsPlayerCharacter,
    int CurrentHp,
    int MaxHp,
    int TemporaryHp,
    int Initiative,
    bool IsDowned,
    bool IsDead,
    IReadOnlyList<ConditionInstanceDto> Conditions,
    DeathSaveStateDto? DeathSaves);

/// <summary>API-facing view of an applied condition instance.</summary>
public sealed record ConditionInstanceDto(
    string Name,
    int? RemainingRounds,
    int AppliedOnRound,
    string? EndCondition);

/// <summary>API-facing view of a combatant's death-save tracking.</summary>
public sealed record DeathSaveStateDto(
    int Successes,
    int Failures,
    bool IsStable,
    bool IsDead);
