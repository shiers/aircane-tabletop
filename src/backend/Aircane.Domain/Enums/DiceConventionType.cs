namespace Aircane.Domain.Enums;

/// <summary>
/// Defines the type of dice convention used by a game system.
/// </summary>
public enum DiceConventionType
{
    /// <summary>Single die plus modifier (d20 systems like D&amp;D, Pathfinder).</summary>
    SingleDieModifier,

    /// <summary>Dice pool with success counting (Shadowrun, World of Darkness).</summary>
    DicePoolSuccess,

    /// <summary>Fixed dice with stat modifier and threshold bands (PbtA).</summary>
    FixedDiceThreshold,

    /// <summary>Fudge/FATE dice (-1, 0, +1).</summary>
    Fudge,

    /// <summary>Step dice with variable die size per trait (Savage Worlds).</summary>
    StepDice,

    /// <summary>Percentile/d100 systems (BRP, Call of Cthulhu).</summary>
    Percentile,

    /// <summary>Generic/custom dice expressions.</summary>
    Expression
}
