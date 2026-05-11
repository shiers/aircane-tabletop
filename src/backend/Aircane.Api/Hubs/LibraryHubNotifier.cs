using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Sends <c>ImportStatusUpdated</c> SignalR events to all clients in the "library" group.
/// </summary>
public sealed class LibraryHubNotifier : ILibraryHubNotifier
{
    private readonly IHubContext<LibraryHub> _hubContext;

    public LibraryHubNotifier(IHubContext<LibraryHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc />
    public Task NotifyImportStatusUpdatedAsync(ImportStatusDto status, CancellationToken ct = default)
    {
        return _hubContext.Clients
            .Group("library")
            .SendAsync("ImportStatusUpdated", status, ct);
    }
}
