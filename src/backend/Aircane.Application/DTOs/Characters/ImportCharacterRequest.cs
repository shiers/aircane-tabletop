namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Request to import a character from a PDF or JSON file.
/// </summary>
public sealed record ImportCharacterRequest(
    string OriginalFileName,
    Stream FileContent,
    string GameSystem,
    string Ruleset,
    Guid? CampaignId = null,
    Guid? OwnerParticipantId = null,
    Guid? TemplateId = null);
