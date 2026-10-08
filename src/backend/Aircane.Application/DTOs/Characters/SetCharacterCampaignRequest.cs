namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request body for PUT /api/characters/{id}/campaign.
/// Assigns, reassigns, or (with a null <see cref="CampaignId"/>) unassigns a character's campaign.
/// </summary>
public sealed record SetCharacterCampaignRequest(
    /// <summary>The target campaign id, or null to unassign the character from any campaign.</summary>
    Guid? CampaignId);
