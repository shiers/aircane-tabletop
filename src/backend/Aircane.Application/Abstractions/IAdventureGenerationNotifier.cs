using Aircane.Application.DTOs.Adventures;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Pushes adventure-generation progress to connected clients via SignalR. Generation is not tied to
/// a play session, so events go to the host-only "adventure-generation" group.
/// </summary>
public interface IAdventureGenerationNotifier
{
    /// <summary>Broadcasts <c>AdventureGenerationProgress</c>.</summary>
    Task NotifyProgressAsync(AdventureGenerationProgressNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts <c>AdventureGenerationCompleted</c>.</summary>
    Task NotifyCompletedAsync(AdventureGenerationCompletedNotification notification, CancellationToken ct = default);

    /// <summary>Broadcasts <c>AdventureGenerationFailed</c>.</summary>
    Task NotifyFailedAsync(AdventureGenerationFailedNotification notification, CancellationToken ct = default);
}
