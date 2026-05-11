using Aircane.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// SignalR hub for library and document import events.
/// Clients join the "library" group to receive broadcast import status updates.
/// Only the host can subscribe to library events.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public sealed class LibraryHub : Hub
{
    /// <summary>
    /// Called by clients to subscribe to library import events.
    /// Adds the caller to the shared "library" group.
    /// </summary>
    public async Task JoinLibraryGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "library");
    }

    /// <summary>
    /// Called by clients to unsubscribe from library import events.
    /// Removes the caller from the shared "library" group.
    /// </summary>
    public async Task LeaveLibraryGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "library");
    }
}
