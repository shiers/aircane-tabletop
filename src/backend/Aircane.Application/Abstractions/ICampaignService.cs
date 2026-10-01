using Aircane.Application.DTOs.Campaigns;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Manages campaign lifecycle: create, list, retrieve, update, and delete.
/// </summary>
public interface ICampaignService
{
    /// <summary>Creates a new campaign and returns its read model.</summary>
    Task<CampaignDto> CreateCampaignAsync(
        CreateCampaignRequest request,
        CancellationToken ct = default);

    /// <summary>Returns all campaigns ordered by creation date descending.</summary>
    Task<IReadOnlyList<CampaignDto>> ListCampaignsAsync(
        CancellationToken ct = default);

    /// <summary>Returns a single campaign by ID, or null if not found.</summary>
    Task<CampaignDto?> GetCampaignAsync(
        Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a partial update to an existing campaign.
    /// Throws <see cref="KeyNotFoundException"/> when the campaign does not exist.
    /// </summary>
    Task<CampaignDto> UpdateCampaignAsync(
        Guid id,
        UpdateCampaignRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a campaign by ID.
    /// Throws <see cref="KeyNotFoundException"/> when the campaign does not exist.
    /// </summary>
    Task DeleteCampaignAsync(
        Guid id,
        CancellationToken ct = default);
}
