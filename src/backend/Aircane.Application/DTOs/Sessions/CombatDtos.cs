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

/// <summary>
/// The encounter as the table may see it (GET /api/sessions/{id}/combat/public): what the players at a real
/// table would know. Public: who is in the fight, the initiative order and values, whose turn it is, the
/// round, everyone's conditions, and whether a combatant is down; the hit points, temporary hit points and
/// death saves of player characters. Not public: any NPC's hit points (current, maximum or temporary) and
/// death saves. The host and a human DM read the full <see cref="EncounterStateDto"/>.
/// </summary>
public sealed record PublicEncounterDto(
    string EncounterId,
    bool IsActive,
    int Round,
    int TurnIndex,
    string? ActiveCombatantId,
    IReadOnlyList<string> InitiativeOrder,
    IReadOnlyList<PublicCombatantDto> Combatants)
{
    /// <summary>Leaves out what the table may not see.</summary>
    public static PublicEncounterDto From(EncounterStateDto full) => new(
        full.EncounterId,
        full.IsActive,
        full.Round,
        full.TurnIndex,
        full.ActiveCombatantId,
        full.InitiativeOrder,
        full.Combatants.Select(PublicCombatantDto.From).ToList());
}

/// <summary>One combatant in <see cref="PublicEncounterDto"/>. Hit points are null for NPCs.</summary>
/// <param name="IsDefeated">Down or dead: visible at the table, unlike how many hit points are left.</param>
public sealed record PublicCombatantDto(
    string Id,
    string Name,
    bool IsPlayerCharacter,
    int Initiative,
    int? CurrentHp,
    int? MaxHp,
    int? TemporaryHp,
    bool IsDefeated,
    IReadOnlyList<ConditionInstanceDto> Conditions,
    DeathSaveStateDto? DeathSaves)
{
    public static PublicCombatantDto From(CombatantDto c) => new(
        c.Id,
        c.Name,
        c.IsPlayerCharacter,
        c.Initiative,
        CurrentHp: c.IsPlayerCharacter ? c.CurrentHp : null,
        MaxHp: c.IsPlayerCharacter ? c.MaxHp : null,
        TemporaryHp: c.IsPlayerCharacter ? c.TemporaryHp : null,
        IsDefeated: c.IsDowned || c.IsDead,
        c.Conditions,
        DeathSaves: c.IsPlayerCharacter ? c.DeathSaves : null);
}
