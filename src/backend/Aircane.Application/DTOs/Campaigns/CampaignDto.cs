using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Campaigns;

/// <summary>
/// Read model returned for campaign queries.
/// </summary>
public sealed record CampaignDto(
    Guid Id,
    string Name,
    string GameSystem,
    string Ruleset,
    AiRole AiRole,
    AiAuthority AiAuthority,
    Guid? ActiveAdventureId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
