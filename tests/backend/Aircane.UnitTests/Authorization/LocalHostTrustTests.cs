using System.Net;
using System.Security.Claims;
using Aircane.Api.Authorization;
using Aircane.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Aircane.UnitTests.Authorization;

/// <summary>
/// <see cref="LocalHostTrust"/> decides who desktop mode treats as the host without a token. The
/// backend binds every interface and cloudflared forwards tunnel traffic from 127.0.0.1, so only a
/// direct loopback request qualifies.
/// </summary>
public sealed class LocalHostTrustTests
{
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
    [InlineData("Forwarded")]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Forwarded-Host")]
    [InlineData("X-Forwarded-Proto")]
    [InlineData("X-Real-IP")]
    [InlineData("True-Client-IP")]
    [InlineData("CF-Connecting-IP")]
    [InlineData("CF-Ray")]
    public void LoopbackPeer_WithAProxyHeader_IsNotTrusted(string header)
    {
        var context = LocalHostHttpContext.Create();
        context.Request.Headers[header] = "203.0.113.7";

        Assert.False(LocalHostTrust.IsTrustedLocalCaller(context));
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

    // ── The desktop-mode requirement ──────────────────────────────────────────

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

    [Fact]
    public void AnonymousHostRule_LocalHostOnly_TrustsOnlyTheLocalHost()
    {
        Assert.True(AnonymousHostRule.LocalHostOnly.IsHost(LocalHostHttpContext.Create()));
        Assert.False(AnonymousHostRule.LocalHostOnly.IsHost(LocalHostHttpContext.From(IPAddress.Parse("192.168.1.50"))));
        Assert.True(new AnonymousHostRule(trustAnyone: true).IsHost(LocalHostHttpContext.From(IPAddress.Parse("192.168.1.50"))));
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
