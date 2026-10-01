using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Defines the structure of character sheets for a game system, including sections and fields.
/// </summary>
public record CharacterSchema
{
    /// <summary>Ordered list of sections that make up the character sheet.</summary>
    public IReadOnlyList<CharacterSchemaSection> Sections { get; init; } = [];
}

/// <summary>
/// A section within a character schema (e.g., "Basic Information", "Ability Scores", "Combat").
/// </summary>
public record CharacterSchemaSection
{
    /// <summary>Unique identifier for this section.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display label for this section.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Fields within this section.</summary>
    public IReadOnlyList<CharacterSchemaField> Fields { get; init; } = [];

    /// <summary>Conditional visibility rule - section only appears when condition is met.</summary>
    public VisibilityCondition? VisibleWhen { get; init; }
}

/// <summary>
/// A field within a character schema section.
/// </summary>
public record CharacterSchemaField
{
    /// <summary>Unique identifier for this field.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The data type of this field.</summary>
    public CharacterFieldType Type { get; init; }

    /// <summary>Display label for this field.</summary>
    public string? Label { get; init; }

    /// <summary>Whether this field is required.</summary>
    public bool Required { get; init; }

    /// <summary>Minimum value (for number fields).</summary>
    public int? Min { get; init; }

    /// <summary>Maximum value (for number fields).</summary>
    public int? Max { get; init; }

    /// <summary>Available options (for enum fields).</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Formula expression for calculated fields (e.g., "floor((str - 10) / 2)").</summary>
    public string? Formula { get; init; }

    /// <summary>Reference to the max field for resource pool fields.</summary>
    public string? MaxField { get; init; }

    /// <summary>Item schema for repeating fields (field name to type mapping).</summary>
    public IReadOnlyDictionary<string, string>? ItemSchema { get; init; }

    /// <summary>Conditional visibility rule for this field.</summary>
    public VisibilityCondition? VisibleWhen { get; init; }
}

/// <summary>
/// Defines a conditional visibility rule for sections or fields.
/// </summary>
public record VisibilityCondition
{
    /// <summary>The field ID to evaluate.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>The set of values that make this element visible.</summary>
    public IReadOnlyList<string>? In { get; init; }
}
