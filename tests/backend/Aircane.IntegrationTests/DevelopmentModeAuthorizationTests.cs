using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Development mode used to open every policy so a developer could work without tokens. The LAN
/// hosting guide has people run it on <c>0.0.0.0</c>, so that handed Host to the whole network. It
/// now follows the same rule as desktop mode: a direct request from this machine is the host,
/// everyone else needs a participant token.
/// </summary>
public sealed class DevelopmentModeAuthorizationTests : IClassFixture<DevelopmentModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");

    private readonly DevelopmentModeWebApplicationFactory _factory;

    public DevelopmentModeAuthorizationTests(DevelopmentModeWebApplicationFactory factory) => _factory = factory;

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private HttpClient Lan(string? token = null)
    {
        var client = _factory.CreateClientFrom(LanAddress);
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(SessionDto Session, string Token)> JoinAsLanPlayerAsync()
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Dev Auth", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = false }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();

        using var lan = Lan();
        var joined = await (await lan.PostAsJsonAsync($"/api/sessions/{session!.Id}/join",
            new { DisplayName = "Lan Player", InviteCode = session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>();
        return (session, joined!.ParticipantToken);
    }

    public static TheoryData<string, string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/ai/settings", "HostOnly" },
        { "GET", "/api/campaigns", "DmOrHost" },
        { "GET", "/api/adventure-sessions/in-progress", "Authenticated + Host check" },
        { "GET", "/api/ai/roles", "Authenticated" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task LanCaller_NoToken_IsRejected(string method, string url, string policy)
    {
        using var lan = Lan();

        using var response = await lan.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{method} {url} ({policy}) from a LAN address with no token returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task LanCaller_NoToken_CannotEndTheDevelopersSession()
    {
        var (session, _) = await JoinAsLanPlayerAsync();
        using var lan = Lan();

        using var response = await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AircaneDbContext>()
            .Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Active, saved.Status);
    }

    [Theory]
    [InlineData("GET", "/api/ai/settings")]
    [InlineData("GET", "/api/campaigns")]
    [InlineData("GET", "/api/adventure-sessions/in-progress")]
    public async Task LanPlayer_HostAndDmEndpoints_AreForbidden(string method, string url)
    {
        var (_, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);

        using var response = await lan.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LanPlayer_AuthenticatedEndpoints_StillWork()
    {
        var (session, token) = await JoinAsLanPlayerAsync();
        using var lan = Lan(token);

        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/ai/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync($"/api/sessions/{session.Id}")).StatusCode);
    }

    // docs/setup/lan-hosting.md has players load the SPA from the Vite dev server, whose proxy calls
    // the backend from 127.0.0.1. With xfwd (vite.config.ts) it reports who the browser really is.
    [Fact]
    public async Task ViteProxy_ForALanPlayersBrowser_IsRejected()
    {
        using var proxied = _factory.CreateClientFrom(IPAddress.Loopback);
        proxied.DefaultRequestHeaders.Host = "localhost:5000";
        proxied.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.1.77");
        proxied.DefaultRequestHeaders.Add("X-Forwarded-Host", "192.168.1.42:5173");
        proxied.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.GetAsync("/api/ai/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.GetAsync("/api/adventure-sessions/in-progress")).StatusCode);
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task ViteProxy_ForTheDevelopersOwnBrowser_IsTheDeveloper(string browserAddress)
    {
        using var proxied = _factory.CreateClientFrom(IPAddress.Loopback);
        proxied.DefaultRequestHeaders.Host = "localhost:5000";
        proxied.DefaultRequestHeaders.Add("X-Forwarded-For", browserAddress);
        proxied.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost:5173");
        proxied.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        Assert.Equal(HttpStatusCode.OK, (await proxied.GetAsync("/api/ai/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await proxied.GetAsync("/api/adventure-sessions/in-progress")).StatusCode);
    }

    [Fact]
    public async Task LoopbackCaller_NoToken_IsTheDeveloper()
    {
        using var host = Host();

        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/ai/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/adventure-sessions/in-progress")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/ai/roles")).StatusCode);
    }
}
