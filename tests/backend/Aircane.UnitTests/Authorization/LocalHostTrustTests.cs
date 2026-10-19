using System.Net;
using System.Security.Claims;
using Aircane.Api.Authorization;
using Aircane.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Aircane.UnitTests.Authorization;

/// <summary>
/// <see cref="LocalHostTrust"/> decides who desktop mode and Development treat as the host without a
/// token. The backend can bind every interface, and a proxy on this machine (the Cloudflare tunnel,
/// the Vite dev server) forwards other people's traffic from 127.0.0.1, so a loopback peer alone is
/// not enough.
/// </summary>
public sealed class LocalHostTrustTests
{
    // ── The peer and the host name ────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1", "localhost")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("::1", "localhost")]
    [InlineData("::1", "[::1]")]
    [InlineData("::ffff:127.0.0.1", "localhost")]
    public void DirectLoopbackRequest_IsTrusted(string remoteIp, string host)
    {
        Assert.True(LocalHostTrust.IsTrustedLocalCaller(LocalHostHttpContext.From(IPAddress.Parse(remoteIp), host: host)));
    }

    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("10.0.0.7")]
    [InlineData("172.17.0.1")]   // Docker gateway
    [InlineData("::ffff:192.168.1.50")]
    public void NonLoopbackPeer_IsNotTrusted(string remoteIp)
    {
        Assert.False(LocalHostTrust.IsTrustedLocalCaller(LocalHostHttpContext.From(IPAddress.Parse(remoteIp), host: "localhost")));
    }

    [Fact]
    public void UnknownPeer_OrNoContext_IsNotTrusted()
    {
        Assert.False(LocalHostTrust.IsTrustedLocalCaller(new DefaultHttpContext()));
        Assert.False(LocalHostTrust.IsTrustedLocalCaller(null));
    }

    [Theory]
    [InlineData("abc.trycloudflare.com")]
    [InlineData("192.168.1.10")]
    [InlineData("")]
    public void LoopbackPeer_ForANonLocalHostName_IsNotTrusted(string host)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Host = string.IsNullOrEmpty(host) ? default : new HostString(host);

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // ── A proxy reporting a client that is not this machine ───────────────────

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.7")]
    [InlineData("Forwarded", "for=\"[2001:db8::1]:4711\";proto=https")]
    [InlineData("Forwarded", "for=127.0.0.1, for=203.0.113.7")]
    [InlineData("X-Forwarded-For", "203.0.113.7")]
    [InlineData("X-Forwarded-For", "127.0.0.1, 203.0.113.7")]
    [InlineData("X-Forwarded-For", "203.0.113.7, 127.0.0.1")]
    [InlineData("X-Forwarded-For", "192.168.1.50:51234")]
    [InlineData("X-Real-IP", "203.0.113.7")]
    [InlineData("True-Client-IP", "203.0.113.7")]
    [InlineData("CF-Connecting-IP", "203.0.113.7")]
    public void LoopbackPeer_ReportedForANonLocalClient_IsNotTrusted(string header, string value)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers[header] = value;

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // A value that cannot be read as an address is not "loopback" either.
    [Theory]
    [InlineData("X-Forwarded-For", "unknown")]
    [InlineData("X-Forwarded-For", "_hidden")]
    [InlineData("Forwarded", "for=_hidden")]
    [InlineData("Forwarded", "for=unknown")]
    [InlineData("X-Real-IP", "not-an-ip")]
    public void LoopbackPeer_WithAnUnreadableForwardedAddress_IsNotTrusted(string header, string value)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers[header] = value;

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // ── A proxy reporting this machine (the Vite dev server with xfwd) ────────

    [Theory]
    [InlineData("X-Forwarded-For", "::1")]
    [InlineData("X-Forwarded-For", "127.0.0.1")]
    [InlineData("X-Forwarded-For", "::ffff:127.0.0.1")]
    [InlineData("X-Forwarded-For", "127.0.0.1, ::1")]
    [InlineData("X-Forwarded-For", "[::1]:51234")]
    [InlineData("Forwarded", "for=127.0.0.1")]
    [InlineData("Forwarded", "for=\"[::1]:4711\";proto=http")]
    public void LoopbackPeer_ReportedForTheLocalMachine_IsTrusted(string header, string value)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers[header] = value;

        Assert.True(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    [Fact]
    public void ViteDevProxy_ForTheDevelopersOwnBrowser_IsTrusted()
    {
        // http-proxy with xfwd: true; browser on localhost:5173, backend reached on localhost:5000.
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["X-Forwarded-For"] = "::1";
        context.Request.Headers["X-Forwarded-Host"] = "localhost:5173";
        context.Request.Headers["X-Forwarded-Proto"] = "http";
        context.Request.Headers["X-Forwarded-Port"] = "5173";

        Assert.True(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // The same proxy, but the request came from a player's phone: this used to look like the host.
    [Fact]
    public void ViteDevProxy_ForALanPlayersBrowser_IsNotTrusted()
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["X-Forwarded-For"] = "192.168.1.77";
        context.Request.Headers["X-Forwarded-Host"] = "192.168.1.42:5173";
        context.Request.Headers["X-Forwarded-Proto"] = "http";

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    [Theory]
    [InlineData("localhost:5173")]
    [InlineData("127.0.0.1:5173")]
    [InlineData("[::1]:5173")]
    public void ForwardedHostThatIsLocal_IsTrusted(string forwardedHost)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["X-Forwarded-Host"] = forwardedHost;

        Assert.True(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("203.0.113.7:5173")]
    [InlineData("192.168.1.42:5173")]
    public void ForwardedHostThatIsNotLocal_IsNotTrusted(string forwardedHost)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["X-Forwarded-Host"] = forwardedHost;

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // ── The Cloudflare tunnel ─────────────────────────────────────────────────

    // Cloudflare's edge adds these to every request and appends the real client to X-Forwarded-For.
    [Fact]
    public void CloudflareTunnelRequest_IsNotTrusted()
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Host = new HostString("abc-def.trycloudflare.com");
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        context.Request.Headers["CF-Ray"] = "8a1b2c3d4e5f-AMS";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // Each signal alone is enough, so a tool that hides one still fails on another.
    [Fact]
    public void CloudflareTunnelRequest_WithTheHostNameRewrittenToLocalhost_IsStillNotTrusted()
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        context.Request.Headers["CF-Ray"] = "8a1b2c3d4e5f-AMS";

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    [Fact]
    public void CloudflareTunnelRequest_WithTheForwardedHeadersStripped_IsStillNotTrusted()
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Host = new HostString("abc-def.trycloudflare.com");

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    [Fact]
    public void CfRayAlone_IsNotTrusted()
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["CF-Ray"] = "8a1b2c3d4e5f-AMS";

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
    }

    // ── The desktop-mode and Development requirement ──────────────────────────

    [Theory]
    [InlineData(null, true)]        // the local host, no token
    [InlineData("Player", true)]    // any role is fine for "authenticated"
    public async Task Authenticated_LocalHost_Succeeds(string? role, bool expected)
    {
        Assert.Equal(expected, await SucceedsAsync(new LocalHostOrRoleRequirement(), role, LocalHostHttpContext.Create(Principal(role))));
    }

    [Theory]
    [InlineData(null, false)]       // a LAN caller with no token
    [InlineData("Player", true)]    // a LAN player with a token
    public async Task Authenticated_Remote_NeedsAToken(string? role, bool expected)
    {
        var remote = LocalHostHttpContext.From(IPAddress.Parse("192.168.1.50"), Principal(role));

        Assert.Equal(expected, await SucceedsAsync(new LocalHostOrRoleRequirement(), role, remote));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Player", false)]
    [InlineData("Spectator", false)]
    [InlineData("Host", true)]
    public async Task HostOnly_Remote_NeedsAHostToken(string? role, bool expected)
    {
        var remote = LocalHostHttpContext.From(IPAddress.Parse("192.168.1.50"), Principal(role));

        Assert.Equal(expected, await SucceedsAsync(new LocalHostOrRoleRequirement("Host"), role, remote));
    }

    [Fact]
    public async Task HostOnly_LocalHost_Succeeds()
    {
        Assert.True(await SucceedsAsync(new LocalHostOrRoleRequirement("Host"), null, LocalHostHttpContext.Create()));
    }

    // A proxied LAN player is not the host even though the peer is loopback.
    [Fact]
    public async Task HostOnly_ProxiedLanPlayer_IsRejected()
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers["X-Forwarded-For"] = "192.168.1.77";

        Assert.False(await SucceedsAsync(new LocalHostOrRoleRequirement("Host"), null, context));
    }

    private static ClaimsPrincipal Principal(string? role) => role is null
        ? new ClaimsPrincipal(new ClaimsIdentity())
        : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));

    private static async Task<bool> SucceedsAsync(LocalHostOrRoleRequirement requirement, string? role, HttpContext httpContext)
    {
        var context = new AuthorizationHandlerContext([requirement], Principal(role), httpContext);
        await new LocalHostOrRoleAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
