using Aircane.Application.DTOs.CampaignState;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Maintains campaign state snapshots, applies commands, appends events,
/// supports undo, and controls reveal state.
/// </summary>
public interface ICampaignStateService
{
    /// <summary>
    /// Loads the current state snapshot for a campaign.
    /// </summary>
    Task<CampaignStateDto> LoadStateAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a named command to the campaign state, persists the resulting
    /// state snapshot, and appends a reversible event to the log.
    /// </summary>
    Task<CampaignStateDto> ApplyCommandAsync(
        ApplyCommandRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a raw event to the campaign event log without modifying the state snapshot.
    /// </summary>
    Task<CampaignEventDto> AppendEventAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes the last reversible command and returns the restored state.
    /// </summary>
    Task<CampaignStateDto> UndoLastCommandAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the event log for a campaign, ordered by most recent first.
    /// </summary>
    Task<IReadOnlyList<CampaignEventDto>> GetEventLogAsync(
        Guid campaignId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the campaign state by replaying all command events from the event log.
    /// Useful for verification, recovery, and ensuring snapshot consistency.
    /// </summary>
    Task<CampaignStateDto> ReplayEventsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
