using Aircane.Application.DTOs.Characters;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Handles character import, canonical schema mapping, manual editing,
/// and template generation.
/// </summary>
public interface ICharacterService
{
    /// <summary>
    /// Imports a character from a PDF or JSON file, mapping fields to the canonical schema.
    /// </summary>
    Task<CharacterDto> ImportCharacterAsync(
        ImportCharacterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a character from a canonical JSON string.
    /// Validates the JSON against the canonical schema and persists the character on success.
    /// Returns a result object containing either the created character or validation errors.
    /// </summary>
    Task<CharacterImportResult> ImportCharacterFromJsonAsync(
        ImportCharacterJsonRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new character manually from the provided data.
    /// </summary>
    Task<CharacterDto> CreateCharacterAsync(
        CreateCharacterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single character by ID.
    /// </summary>
    Task<CharacterDto?> GetCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates editable fields on an existing character.
    /// </summary>
    Task<CharacterDto> UpdateCharacterAsync(
        Guid characterId,
        UpdateCharacterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a character and unassigns it from any session participants.
    /// </summary>
    Task DeleteCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists characters scoped to a campaign.
    /// Hosts see all characters; players see only their own.
    /// </summary>
    Task<IReadOnlyList<CharacterDto>> ListByCampaignAsync(
        Guid campaignId,
        Guid? requestingParticipantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every character in the global library, regardless of campaign assignment,
    /// ordered by name. Each DTO carries its campaign name, or null when the character is
    /// unassigned or its campaign no longer exists.
    /// </summary>
    Task<IReadOnlyList<CharacterDto>> ListAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns, reassigns, or (with a null <paramref name="campaignId"/>) unassigns a character's campaign.
    /// Only the campaign FK and the update timestamp change; CanonicalJson is never touched and no
    /// schema validation is performed. Returns the updated character.
    /// Throws <see cref="KeyNotFoundException"/> when the character does not exist.
    /// Throws <see cref="ArgumentException"/> when a non-null target campaign does not exist.
    /// </summary>
    Task<CharacterDto> SetCampaignAsync(
        Guid characterId,
        Guid? campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a character import field mapping as a reusable template.
    /// </summary>
    Task<CharacterTemplateDto> SaveTemplateAsync(
        SaveTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all saved character import templates.
    /// </summary>
    Task<IReadOnlyList<CharacterTemplateDto>> ListTemplatesAsync(
        string? gameSystem = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single character template by ID.
    /// </summary>
    Task<CharacterTemplateDto?> GetTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a list of user-confirmed field mappings to the character's CanonicalJson
    /// and marks the character as reviewed.
    /// Returns the updated character on success.
    /// Throws <see cref="KeyNotFoundException"/> when the character does not exist.
    /// Throws <see cref="ArgumentException"/> when a canonical field path is invalid or the value cannot be coerced.
    /// </summary>
    Task<CharacterDto> ApplyFieldMappingsAsync(
        Guid characterId,
        ApplyFieldMappingsRequest request,
        CancellationToken cancellationToken = default);
}
