namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Defines a status effect/condition within a game system, including its mechanical impacts.
/// </summary>
public record ConditionDefinition
{
    /// <summary>The name of the condition (e.g., "Poisoned", "Prone", "Stunned").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Human-readable description of the condition's effects.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Mechanical effects this condition applies.</summary>
    public IReadOnlyList<ConditionEffect> Effects { get; init; } = [];

    /// <summary>How the condition's duration is tracked (e.g., "rounds", "until_save", "until_action", "until_rest").</summary>
    public string DurationType { get; init; } = string.Empty;

    /// <summary>Optional description of how the condition ends (e.g., "Use half movement to stand").</summary>
    public string? EndCondition { get; init; }

    /// <summary>Whether multiple instances of this condition can stack.</summary>
    public bool Stackable { get; init; }
}

/// <summary>
/// A single mechanical effect of a condition (e.g., disadvantage on attack rolls).
/// </summary>
public record ConditionEffect
{
    /// <summary>The type of effect (e.g., "roll_modifier", "grant_advantage", "damage_resistance").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The scope this effect applies to (e.g., "attack_rolls", "ability_checks", "melee_attacks_against").</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>The specific effect value (e.g., "disadvantage", "advantage", "immunity").</summary>
    public string? Effect { get; init; }
}
