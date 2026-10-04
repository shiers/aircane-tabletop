namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to import a character from a canonical JSON string.
/// Used by the POST /api/characters/import endpoint.
/// </summary>
public sealed record ImportCharacterJsonRequest(
    /// <summary>The raw canonical character JSON content.</summary>
    string CanonicalJson,
    /// <summary>
    /// Game system, e.g. "D&amp;D 5e". Required for the legacy canonical-JSON import; omitted on the
    /// source-adapter path (where the system is resolved from the detected source).
    /// </summary>
    string? GameSystem = null,
    /// <summary>
    /// Ruleset, e.g. "2014". Required for the legacy canonical-JSON import; omitted on the
    /// source-adapter path.
    /// </summary>
    string? Ruleset = null,
    /// <summary>Optional campaign to associate the character with.</summary>
    Guid? CampaignId = null,
    /// <summary>Optional participant who owns the character.</summary>
    Guid? OwnerParticipantId = null,
    /// <summary>Original filename for display purposes (e.g. "aldric.json").</summary>
    string? OriginalFileName = null,
    /// <summary>Optional game system definition to force when importing via a source adapter.</summary>
    Guid? GameSystemDefinitionId = null);
