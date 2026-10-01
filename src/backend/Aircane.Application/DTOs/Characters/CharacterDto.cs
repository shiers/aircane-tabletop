namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Represents a character sheet returned to callers.
/// </summary>
public sealed record CharacterDto(
    Guid Id,
    Guid? CampaignId,
    Guid? OwnerParticipantId,
    string Name,
    string GameSystem,
    string Ruleset,
    int Level,
    string CanonicalJson,
    string CurrentStateJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
