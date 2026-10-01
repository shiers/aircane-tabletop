namespace Aircane.Application.DTOs.GameSystems;

/// <summary>
/// Request body for migrating a campaign to a different game system definition version.
/// </summary>
public sealed record MigrateSystemRequest(
    Guid GameSystemDefinitionId);

/// <summary>
/// Response for a system migration operation.
/// </summary>
public sealed record MigrateSystemResponse(
    Guid CampaignId,
    Guid NewGameSystemDefinitionId,
    string NewGameSystemName,
    string NewVersion,
    IReadOnlyList<string> Warnings);
