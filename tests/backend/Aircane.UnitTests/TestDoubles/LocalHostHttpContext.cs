using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Aircane.UnitTests.TestDoubles;

/// <summary>
/// HTTP contexts for controller tests. A caller with no token is the host only when the request is a
/// direct loopback request (see <c>LocalHostTrust</c>), so tests that act as the desktop host must
/// say where the request comes from; a bare <see cref="DefaultHttpContext"/> has no address.
/// </summary>
public static class LocalHostHttpContext
{
    /// <summary>A request from the host's own machine: loopback peer, <c>localhost</c> host name.</summary>
    public static DefaultHttpContext Create(ClaimsPrincipal? user = null)
        => From(IPAddress.Loopback, user, "localhost");

    /// <summary>A request from <paramref name="remoteIp"/> (for example a LAN player's machine).</summary>
    public static DefaultHttpContext From(IPAddress remoteIp, ClaimsPrincipal? user = null, string host = "192.168.1.10")
    {
        var context = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        context.Connection.RemoteIpAddress = remoteIp;
        context.Request.Host = new HostString(host, 5000);
        return context;
    }
}
