namespace Aircane.Application.DTOs.GameSystems;

/// <summary>
/// A starter template for creating a new Game System Definition.
/// </summary>
public sealed record GameSystemTemplateDto(
    string Id,
    string Name,
    string Description,
    string Genre,
    string DefinitionJson);
