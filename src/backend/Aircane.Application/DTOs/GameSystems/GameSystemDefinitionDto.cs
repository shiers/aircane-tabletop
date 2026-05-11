namespace Aircane.Application.DTOs.GameSystems;

/// <summary>
/// Full detail DTO for a Game System Definition.
/// </summary>
public sealed record GameSystemDefinitionDto(
    Guid Id,
    string Identifier,
    string Name,
    string Version,
    int SchemaVersion,
    string? Publisher,
    string? Genre,
    string? Description,
    string License,
    IReadOnlyList<string> Tags,
    bool IsActive,
    bool IsBuiltIn,
    string DefinitionJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Summary DTO for listing game system definitions without full JSON.
/// </summary>
public sealed record GameSystemDefinitionSummaryDto(
    Guid Id,
    string Identifier,
    string Name,
    string Version,
    string? Genre,
    string License,
    bool IsBuiltIn,
    bool IsActive);
