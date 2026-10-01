namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to save a character import mapping as a reusable template.
/// </summary>
/// <param name="Name">Display name for the template.</param>
/// <param name="GameSystem">Game system the template targets (e.g. "D&amp;D 5e").</param>
/// <param name="Ruleset">Ruleset edition (e.g. "2014").</param>
/// <param name="FieldDefinitions">Ordered list of field definitions captured from the import mapping.</param>
/// <param name="SourceCharacterId">Optional ID of the character whose mapping was used to build this template.</param>
public sealed record SaveTemplateRequest(
    string Name,
    string GameSystem,
    string Ruleset,
    IReadOnlyList<TemplateFieldDefinition> FieldDefinitions,
    Guid? SourceCharacterId = null);
