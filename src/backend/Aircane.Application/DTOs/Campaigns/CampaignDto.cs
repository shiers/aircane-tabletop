using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Campaigns;

/// <summary>
/// Read model returned for campaign queries.
/// </summary>
/// <param name="GameSystemDefinitionId">
/// The game system definition the campaign is bound to, or null when it is not bound (the campaign then has only
/// its <c>GameSystem</c> and <c>Ruleset</c> strings).
/// </param>
public sealed record CampaignDto(
    Guid Id,
    string Name,
    string GameSystem,
    string Ruleset,
    AiRole AiRole,
    AiAuthority AiAuthority,
    Guid? ActiveAdventureId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? GameSystemDefinitionId = null);
