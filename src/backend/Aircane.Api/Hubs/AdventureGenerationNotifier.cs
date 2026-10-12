using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Sends adventure-generation events to the "adventure-generation" group of <see cref="LibraryHub"/>
/// (host-only, like import status). Clients join with <c>JoinAdventureGenerationGroup</c>.
/// </summary>
public sealed class AdventureGenerationNotifier : IAdventureGenerationNotifier
{
    private readonly IHubContext<LibraryHub> _hubContext;

    public AdventureGenerationNotifier(IHubContext<LibraryHub> hubContext) => _hubContext = hubContext;

    /// <inheritdoc />
    public Task NotifyProgressAsync(AdventureGenerationProgressNotification n, CancellationToken ct = default)
        => Group().SendAsync("AdventureGenerationProgress", n, ct);

    /// <inheritdoc />
    public Task NotifyCompletedAsync(AdventureGenerationCompletedNotification n, CancellationToken ct = default)
        => Group().SendAsync("AdventureGenerationCompleted", n, ct);

    /// <inheritdoc />
    public Task NotifyFailedAsync(AdventureGenerationFailedNotification n, CancellationToken ct = default)
        => Group().SendAsync("AdventureGenerationFailed", n, ct);

    private IClientProxy Group() => _hubContext.Clients.Group(LibraryHub.AdventureGenerationGroup);
}
