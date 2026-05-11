using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Campaigns;

/// <summary>
/// Request body for updating an existing campaign.
/// All fields are optional; only provided (non-null) fields are applied.
/// </summary>
public sealed record UpdateCampaignRequest(
    string? Name = null,
    string? GameSystem = null,
    string? Ruleset = null,
    AiRole? AiRole = null,
    AiAuthority? AiAuthority = null,
    Guid? ActiveAdventureId = null,
    Guid? GameSystemDefinitionId = null);
