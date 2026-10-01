namespace Aircane.Domain.Enums;

/// <summary>
/// Defines the types of fields available in a character schema.
/// </summary>
public enum CharacterFieldType
{
    /// <summary>Free-form text input.</summary>
    Text,

    /// <summary>Numeric value with optional min/max.</summary>
    Number,

    /// <summary>True/false toggle.</summary>
    Boolean,

    /// <summary>Selection from a predefined list of options.</summary>
    Enum,

    /// <summary>A dice expression (e.g., 2d6+3).</summary>
    DiceExpression,

    /// <summary>A list of text values.</summary>
    List,

    /// <summary>A repeating section with item schema (e.g., spells, inventory).</summary>
    Repeating,

    /// <summary>A resource pool with current and maximum values (e.g., HP, spell slots).</summary>
    ResourcePool,

    /// <summary>A computed field derived from a formula referencing other fields.</summary>
    Calculated,

    /// <summary>A grouped set of related fields.</summary>
    Grouped
}
