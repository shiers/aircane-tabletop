namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// A saved character import template returned to callers.
/// </summary>
/// <param name="Id">Unique template identifier.</param>
/// <param name="Name">Display name for the template.</param>
/// <param name="GameSystem">Game system the template targets.</param>
/// <param name="Ruleset">Ruleset edition.</param>
/// <param name="FieldDefinitions">Ordered list of field definitions.</param>
/// <param name="CreatedAt">When the template was first saved.</param>
/// <param name="UpdatedAt">When the template was last modified.</param>
public sealed record CharacterTemplateDto(
    Guid Id,
    string Name,
    string GameSystem,
    string Ruleset,
    IReadOnlyList<TemplateFieldDefinition> FieldDefinitions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
