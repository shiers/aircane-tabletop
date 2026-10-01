using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Campaigns;

/// <summary>
/// Request body for creating a new campaign.
/// Provide either GameSystemDefinitionId (preferred) or legacy GameSystem/Ruleset strings.
/// </summary>
public sealed record CreateCampaignRequest(
    string Name,
    string GameSystem,
    string Ruleset,
    AiRole AiRole = AiRole.Assistant,
    AiAuthority AiAuthority = AiAuthority.SuggestOnly,
    Guid? GameSystemDefinitionId = null);
