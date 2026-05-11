namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to import a character from a canonical JSON string.
/// Used by the POST /api/characters/import endpoint.
/// </summary>
public sealed record ImportCharacterJsonRequest(
    /// <summary>The raw canonical character JSON content.</summary>
    string CanonicalJson,
    /// <summary>Game system, e.g. "D&amp;D 5e".</summary>
    string GameSystem,
    /// <summary>Ruleset, e.g. "2014".</summary>
    string Ruleset,
    /// <summary>Optional campaign to associate the character with.</summary>
    Guid? CampaignId = null,
    /// <summary>Optional participant who owns the character.</summary>
    Guid? OwnerParticipantId = null,
    /// <summary>Original filename for display purposes (e.g. "aldric.json").</summary>
    string? OriginalFileName = null);
