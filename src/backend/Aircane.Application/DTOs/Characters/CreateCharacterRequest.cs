using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to manually create a new character.
/// </summary>
public sealed record CreateCharacterRequest(
    string Name,
    string GameSystem,
    string Ruleset,
    int Level,
    string CanonicalJson,
    Guid? CampaignId = null,
    Guid? OwnerParticipantId = null,
    CharacterRole Role = CharacterRole.Player);
