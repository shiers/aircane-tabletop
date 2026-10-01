using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Sends real-time import status notifications to connected clients via SignalR.
/// </summary>
public interface ILibraryHubNotifier
{
    /// <summary>
    /// Broadcasts an <c>ImportStatusUpdated</c> event to all clients in the "library" group.
    /// </summary>
    /// <param name="status">The current import status payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyImportStatusUpdatedAsync(ImportStatusDto status, CancellationToken ct = default);
}
