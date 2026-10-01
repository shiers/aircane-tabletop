namespace Aircane.Application.DTOs.CampaignState;

/// <summary>
/// A snapshot of the current campaign state.
/// </summary>
public sealed record CampaignStateDto(
    Guid CampaignId,
    Guid? ActiveSessionId,
    string? CurrentSceneJson,
    string StateJson,
    DateTimeOffset SnapshotAt);
