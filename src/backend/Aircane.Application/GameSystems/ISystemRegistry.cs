using Aircane.Domain.Entities.GameSystems;
using FluentValidation.Results;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Manages installed Game System Definitions: CRUD, import/export, validation, and versioning.
/// </summary>
public interface ISystemRegistry
{
    /// <summary>Loads a definition by its primary key, hydrating rich domain properties from DefinitionJson.</summary>
    Task<GameSystemDefinition> GetByIdAsync(Guid definitionId, CancellationToken ct = default);

    /// <summary>Looks up the campaign's bound definition and returns it fully hydrated.</summary>
    Task<GameSystemDefinition> GetByCampaignAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>Returns lightweight summary DTOs for all active definitions.</summary>
    Task<IReadOnlyList<GameSystemDefinitionSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Parses a definition from a stream, validates it, and persists it.</summary>
    Task<GameSystemDefinition> ImportAsync(Stream definitionFile, string format, CancellationToken ct = default);

    /// <summary>Serializes a stored definition to JSON and returns it as a stream.</summary>
    Task<Stream> ExportAsync(Guid definitionId, string format, CancellationToken ct = default);

    /// <summary>Parses and validates a definition without persisting it.</summary>
    Task<ValidationResult> ValidateAsync(Stream definitionFile, string format, CancellationToken ct = default);

    /// <summary>Validates and persists a new definition, creating an initial version record.</summary>
    Task<Guid> CreateAsync(GameSystemDefinition definition, CancellationToken ct = default);

    /// <summary>Validates and updates an existing definition, creating a new version snapshot.</summary>
    Task UpdateAsync(Guid definitionId, GameSystemDefinition definition, CancellationToken ct = default);

    /// <summary>Deactivates a definition if no active campaigns reference it.</summary>
    Task DeactivateAsync(Guid definitionId, CancellationToken ct = default);
}

/// <summary>
/// Lightweight summary DTO for listing game system definitions without full JSON deserialization.
/// </summary>
public record GameSystemDefinitionSummary
{
    public Guid Id { get; init; }
    public string Identifier { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Genre { get; init; }
    public string License { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
    public bool IsActive { get; init; }
}
