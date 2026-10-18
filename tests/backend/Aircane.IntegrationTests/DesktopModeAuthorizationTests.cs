using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Desktop mode (<c>Aircane:TrustLocalHost</c>) trusts the person at the host machine, and only
/// them. The backend binds every interface so LAN players can reach it, and the Cloudflare tunnel
/// (<c>cloudflared --url http://localhost:port</c>) delivers internet traffic from 127.0.0.1, so
/// "trusted" must mean a direct loopback request, not merely a request with no token.
/// </summary>
public sealed class DesktopModeAuthorizationTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private const string TunnelHost = "test-tunnel.trycloudflare.com";

    private readonly DesktopModeWebApplicationFactory _factory;

    public DesktopModeAuthorizationTests(DesktopModeWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private HttpClient Lan(string? token = null)
    {
        var client = _factory.CreateClientFrom(LanAddress);
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Internet traffic as cloudflared delivers it: a loopback TCP peer, the public tunnel host
    /// name, and the headers Cloudflare's edge adds.
    /// </summary>
    private HttpClient ThroughTunnel()
    {
        var client = _factory.CreateClientFrom(IPAddress.Loopback);
        client.DefaultRequestHeaders.Host = TunnelHost;
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.7");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        return client;
    }

    /// <summary>The desktop host creates a campaign and a LAN session; returns it with its invite code.</summary>
    private async Task<SessionDto> StartSessionAsHostAsync()
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Desktop Auth", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = false }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();
        return session!;
    }

    /// <summary>A LAN player joins with the invite code and gets a Player token.</summary>
    private async Task<(SessionDto Session, string Token)> JoinAsLanPlayerAsync()
    {
        var session = await StartSessionAsHostAsync();
        using var lan = Lan();
        var joined = await (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = "Lan Player", InviteCode = session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>();
        return (session, joined!.ParticipantToken);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url)
        => new(method, url) { Content = method == HttpMethod.Get || method == HttpMethod.Delete ? null : JsonContent.Create(new { }) };

    /// <summary>One endpoint per policy and per kind of Host action (method, url, policy).</summary>
    public static TheoryData<string, string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/ai/settings", "HostOnly" },
        { "DELETE", $"/api/library/documents/{Guid.NewGuid()}", "HostOnly" },
        { "POST", $"/api/sessions/{Guid.NewGuid()}/end", "HostOnly" },
        { "GET", "/api/adventure-sessions/in-progress", "Authenticated + Host check" },
        { "GET", "/api/campaigns", "DmOrHost" },
        { "POST", $"/api/sessions/{Guid.NewGuid()}/ai/proposals/{Guid.NewGuid()}/approve", "DmOrHost" },
        { "GET", "/api/ai/roles", "Authenticated" },
    };

    // ── LAN caller with no token ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task LanCaller_NoToken_IsRejected(string method, string url, string policy)
    {
        using var lan = Lan();

        using var response = await lan.SendAsync(Request(new HttpMethod(method), url));

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{method} {url} ({policy}) from a LAN address with no token returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task LanCaller_NoToken_CannotEndTheHostsSession()
    {
        var session = await StartSessionAsHostAsync();
        using var lan = Lan();

        using var response = await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AircaneDbContext>()
            .Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Active, saved.Status);
    }

    [Fact]
    public async Task LanCaller_NoToken_CannotCreateACampaign()
    {
        using var lan = Lan();

        using var response = await lan.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Hijack", "D&D 5e 2014", "PHB"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── LAN caller with a Player token ────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/ai/settings")]
    [InlineData("GET", "/api/campaigns")]
    [InlineData("GET", "/api/adventure-sessions/in-progress")]
    public async Task LanPlayer_HostAndDmEndpoints_AreForbidden(string method, string url)
    {
        var (_, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);

        using var response = await lan.SendAsync(Request(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LanPlayer_CannotEndTheSession()
    {
        var (session, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);

        using var response = await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LanPlayer_AuthenticatedEndpoints_StillWork()
    {
        var (session, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);

        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/ai/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync($"/api/sessions/{session.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync($"/api/sessions/{session.Id}/participants")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync($"/api/sessions/{session.Id}/rolls")).StatusCode);
    }

    // SessionsController declares HostOnly per action, so the player reads above are not caught by
    // a class-level HostOnly; the host actions next to them must stay Host-only.
    [Fact]
    public async Task LanPlayer_HostSessionActions_AreForbidden()
    {
        var (session, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync($"/api/campaigns/{session.CampaignId}/start-session",
            new { Name = "Mine", AccessMode = (int)SessionAccessMode.LocalLan })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/approve-participant",
            new { ParticipantId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/assign-character",
            new { ParticipantId = Guid.NewGuid(), CharacterId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsync($"/api/sessions/{session.Id}/resume-ai", null)).StatusCode);
    }

    // ── The desktop host itself ───────────────────────────────────────────────

    [Fact]
    public async Task LoopbackCaller_NoToken_IsTheHost()
    {
        var session = await StartSessionAsHostAsync();   // POST /api/campaigns + start-session
        using var host = Host();

        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/ai/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/adventure-sessions/in-progress")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/ai/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.DeleteAsync($"/api/library/documents/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await host.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task LoopbackIPv6Caller_NoToken_IsTheHost()
    {
        using var host = _factory.CreateClientFrom(IPAddress.IPv6Loopback);

        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/ai/settings")).StatusCode);
    }

    // ── Tunnel traffic arrives from loopback but is not the host ──────────────

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task TunnelCaller_NoToken_IsRejected(string method, string url, string policy)
    {
        using var tunnel = ThroughTunnel();

        using var response = await tunnel.SendAsync(Request(new HttpMethod(method), url));

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{method} {url} ({policy}) through the tunnel with no token returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task TunnelCaller_CannotClearTheTunnel()
    {
        using var tunnel = ThroughTunnel();

        using var response = await tunnel.DeleteAsync("/api/sessions/tunnel-url");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.7")]
    [InlineData("Forwarded", "for=203.0.113.7")]
    [InlineData("CF-Connecting-IP", "203.0.113.7")]
    [InlineData("X-Real-IP", "203.0.113.7")]
    public async Task LoopbackCaller_WithAForwardingHeader_IsNotTrusted(string header, string value)
    {
        using var proxied = Host();
        proxied.DefaultRequestHeaders.Add(header, value);

        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.GetAsync("/api/ai/settings")).StatusCode);
    }

    // A same-machine proxy that strips forwarding headers still passes the public host name on.
    [Fact]
    public async Task LoopbackCaller_ForANonLocalHostName_IsNotTrusted()
    {
        using var proxied = Host();
        proxied.DefaultRequestHeaders.Host = TunnelHost;

        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.GetAsync("/api/ai/settings")).StatusCode);
    }

    // Guards the Player-token tests above: they only mean something if the token is really validated.
    [Fact]
    public async Task Harness_PlayerTokensAreValidatedByTheRealJwtScheme()
    {
        var schemes = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        var all = (await schemes.GetAllSchemesAsync()).Select(s => s.Name).ToList();
        Assert.Contains(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, all);
        Assert.Equal(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);

        var (session, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);
        using var reconnect = await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);   // the token itself is valid
    }

    // ── Fallback policy and explicit anonymous endpoints ──────────────────────

    [Fact]
    public void FallbackPolicy_IsSet()
    {
        var options = _factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
    }

    // No endpoint matches, so only the fallback policy applies: closed to LAN callers.
    [Fact]
    public async Task FallbackPolicy_RequestWithNoEndpoint_FailsClosedForLanCallers()
    {
        using var lan = Lan();
        using var host = Host();

        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.GetAsync("/api/no-such-endpoint")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync("/api/no-such-endpoint")).StatusCode);
    }

    [Fact]
    public async Task AnonymousEndpoints_StillWorkFromTheLan()
    {
        var session = await StartSessionAsHostAsync();
        using var lan = Lan();

        var join = await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = "Anon", InviteCode = session.InviteCode });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/sessions/network-info")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/library/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/setup/status")).StatusCode);
        var health = await lan.GetAsync("/api/health");
        Assert.True(health.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable, $"health returned {health.StatusCode}");
        // An invalid body is rejected by the action itself (400), proving it is reachable without
        // a token, and without the request ever reaching the feedback provider.
        Assert.Equal(HttpStatusCode.BadRequest, (await lan.PostAsJsonAsync("/api/feedback", new { })).StatusCode);
    }
}

/// <summary>
/// The desktop sidecar does not set Jwt__SigningKey, so by default no participant token can be
/// validated. Rejections must still be clean 401s, and the local host must still be the host.
/// </summary>
public sealed class DesktopModeWithoutSigningKeyTests : IClassFixture<DesktopModeWithoutSigningKeyFactory>
{
    private readonly DesktopModeWithoutSigningKeyFactory _factory;

    public DesktopModeWithoutSigningKeyTests(DesktopModeWithoutSigningKeyFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/ai/settings")]
    [InlineData("/api/campaigns")]
    [InlineData("/api/ai/roles")]
    public async Task LanCaller_IsRejectedWith401_NotAServerError(string url)
    {
        using var lan = _factory.CreateClientFrom(IPAddress.Parse("192.168.1.50"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task LoopbackCaller_IsStillTheHost()
    {
        using var host = _factory.CreateClientFrom(IPAddress.Loopback);

        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/ai/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/adventure-sessions/in-progress")).StatusCode);
    }
}
