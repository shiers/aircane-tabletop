namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Describes a single field in a character template.
/// </summary>
/// <param name="FieldName">Human-readable label for the field (e.g. "Strength").</param>
/// <param name="CanonicalFieldPath">Dot-separated path in the canonical character schema (e.g. "abilities.strength").</param>
/// <param name="DataType">Expected data type: "string", "int", or "bool".</param>
/// <param name="DisplayGroup">UI grouping label (e.g. "Abilities", "Combat").</param>
/// <param name="IsRequired">Whether the field must be filled when creating a character from this template.</param>
/// <param name="DefaultValue">Optional default value pre-filled in blank characters.</param>
/// <param name="SourceCoordinate">Optional PDF coordinate string (e.g. "page:1,x:120,y:340") from the original import.</param>
public sealed record TemplateFieldDefinition(
    string FieldName,
    string CanonicalFieldPath,
    string DataType,
    string DisplayGroup,
    bool IsRequired,
    string? DefaultValue,
    string? SourceCoordinate);
