using Aircane.Domain.Enums;
using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Result of validating character JSON against a Character Schema.
/// </summary>
public record CharacterValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<CharacterValidationError> Errors { get; init; } = [];
}

/// <summary>
/// A single validation error for a specific field path.
/// </summary>
public record CharacterValidationError
{
    public string FieldPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Describes the form layout for rendering a character sheet in the frontend.
/// </summary>
public record FormDescriptor
{
    public IReadOnlyList<FormSection> Sections { get; init; } = [];
}

/// <summary>
/// A section within the form descriptor.
/// </summary>
public record FormSection
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<FormField> Fields { get; init; } = [];
    public VisibilityCondition? VisibleWhen { get; init; }
}

/// <summary>
/// A field within a form section, containing all information needed for frontend rendering.
/// </summary>
public record FormField
{
    public string Id { get; init; } = string.Empty;
    public CharacterFieldType Type { get; init; }
    public string? Label { get; init; }
    public bool Required { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public string? Formula { get; init; }
    public string? MaxField { get; init; }
    public bool IsReadOnly { get; init; }
}

/// <summary>
/// Result of mapping imported fields to a character schema.
/// </summary>
public record FieldMappingResult
{
    /// <summary>Fields successfully mapped: schema field ID → imported value.</summary>
    public Dictionary<string, string> MappedFields { get; init; } = new();

    /// <summary>Fields that could not be mapped: original key → value.</summary>
    public Dictionary<string, string> UnmappedFields { get; init; } = new();
}
