namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to update an existing character's editable fields.
/// </summary>
public sealed record UpdateCharacterRequest(
    string? Name = null,
    int? Level = null,
    string? CanonicalJson = null,
    string? CurrentStateJson = null,
    Guid? OwnerParticipantId = null,
    Guid? CampaignId = null);
