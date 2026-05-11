using Aircane.Application.DTOs.Adventures;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Service for retrieving and loading generated adventures from the database.
/// </summary>
public interface IAdventureRetrievalService
{
    /// <summary>
    /// Loads a generated adventure by ID, deserializing all JSON columns into the DTO.
    /// </summary>
    /// <param name="adventureId">The adventure ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete adventure DTO, or null if not found.</returns>
    Task<GeneratedAdventureDto?> GetByIdAsync(Guid adventureId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the draft sections of a generated adventure for review.
    /// Returns only the content sections (pitch, outline, scenes, npcs, encounters, treasure, clues)
    /// without the full request/party analysis metadata.
    /// </summary>
    /// <param name="adventureId">The adventure ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The adventure draft DTO, or null if not found.</returns>
    Task<AdventureDraftDto?> GetDraftAsync(Guid adventureId, CancellationToken cancellationToken = default);
}
