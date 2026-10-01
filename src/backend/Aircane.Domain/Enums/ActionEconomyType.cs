namespace Aircane.Domain.Enums;

/// <summary>
/// Defines the type of action economy used by a game system.
/// </summary>
public enum ActionEconomyType
{
    /// <summary>Named action slots (D&amp;D 5e: action, bonus action, reaction).</summary>
    NamedSlots,

    /// <summary>Point pool spent per action.</summary>
    ActionPoints,

    /// <summary>Multiple actions with increasing penalty (Pathfinder 2e MAP).</summary>
    MultiActionPenalty,

    /// <summary>Narrative systems with no mechanical action tracking.</summary>
    Freeform
}
