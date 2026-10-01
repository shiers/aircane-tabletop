namespace Aircane.Application.DTOs.Library;

/// <summary>A game-system alias as returned to the settings UI.</summary>
public sealed record GameSystemAliasDto(
    Guid Id,
    Guid GameSystemDefinitionId,
    string Alias,
    DateTimeOffset CreatedAt);
