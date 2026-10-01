using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Describes a game system's primary dice mechanic and any named custom conventions.
/// </summary>
public record DiceConvention
{
    /// <summary>The type of dice convention (e.g., single_die_modifier, dice_pool_success).</summary>
    public DiceConventionType Type { get; init; }

    /// <summary>A human-readable name for this convention (e.g., "primary", "damage", "hit_dice").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Description of this convention's purpose.</summary>
    public string? Description { get; init; }

    /// <summary>The die used (e.g., "d20", "d6", "d100"). Null for expression-based conventions.</summary>
    public string? Die { get; init; }

    /// <summary>Sources of modifiers applied to the roll (e.g., "ability_modifier", "proficiency_bonus").</summary>
    public IReadOnlyList<string> ModifierSources { get; init; } = [];

    /// <summary>Advantage mechanic: how many dice to roll and which to keep.</summary>
    public KeepDirective? Advantage { get; init; }

    /// <summary>Disadvantage mechanic: how many dice to roll and which to keep.</summary>
    public KeepDirective? Disadvantage { get; init; }

    /// <summary>For dice pool systems: the success threshold (roll >= this value counts as a success).</summary>
    public int? SuccessThreshold { get; init; }

    /// <summary>For exploding dice: the threshold at which dice explode (re-roll and add).</summary>
    public int? ExplodeThreshold { get; init; }

    /// <summary>For step dice: the mapping of trait levels to die sizes.</summary>
    public IReadOnlyList<string>? StepDiceLadder { get; init; }
}

/// <summary>
/// Describes a keep/drop directive for advantage/disadvantage mechanics.
/// </summary>
public record KeepDirective
{
    /// <summary>Number of dice to roll.</summary>
    public int Roll { get; init; }

    /// <summary>Which die to keep: "highest" or "lowest".</summary>
    public string Keep { get; init; } = "highest";
}
