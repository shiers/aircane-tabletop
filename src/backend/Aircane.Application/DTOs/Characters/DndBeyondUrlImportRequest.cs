namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to import a character from a D&amp;D Beyond character URL (or bare numeric id).
/// Used by the POST /api/characters/import/dndbeyond-url endpoint. The character URL is never
/// stored or logged.
/// </summary>
public sealed record DndBeyondUrlImportRequest(
    /// <summary>A full dndbeyond.com/characters/{id} URL or a bare numeric character id.</summary>
    string CharacterUrl,
    /// <summary>Optional game system definition override (defaults to the forced D&amp;D 5e 2014 system).</summary>
    Guid? GameSystemDefinitionId = null,
    /// <summary>Optional campaign to associate the imported character with.</summary>
    Guid? CampaignId = null);
