using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// GET /api/sessions/{id}/join-info is what the join page reads before the player has a token. It
/// must work with no token, return only a name and whether the session is joinable, and say the
/// same thing about every session that cannot be joined.
/// </summary>
public sealed class JoinInfoIntegrationTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private const string NotJoinableBody = "{\"name\":null,\"joinable\":false}";

    private readonly DesktopModeWebApplicationFactory _factory;

    public JoinInfoIntegrationTests(DesktopModeWebApplicationFactory factory) => _factory = factory;

    private async Task<SessionDto> StartSessionAsync(
        SessionAccessMode mode = SessionAccessMode.LocalLan, string name = "Friday Table")
    {
        using var host = _factory.CreateClientFrom(IPAddress.Loopback);
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Secret Campaign Name", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var response = await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = name, AccessMode = (int)mode, RequireHostApproval = false });
        return (await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> JoinInfoAsync(Guid id)
    {
        using var lan = _factory.CreateClientFrom(LanAddress);   // no token
        var response = await lan.GetAsync($"/api/sessions/{id}/join-info");
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response);
    }

    // ── Works with no token, and says only what the form needs ───────────────

    [Fact]
    public async Task JoinableSession_ReturnsNameAndJoinable_WithNoToken()
    {
        var session = await StartSessionAsync();

        var (status, body, _) = await JoinInfoAsync(session.Id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"name\":\"Friday Table\",\"joinable\":true}", body);
    }

    [Fact]
    public async Task Response_HasExactlyTwoFields_AndNothingElseAboutTheSession()
    {
        var session = await StartSessionAsync();
        using var host = _factory.CreateClientFrom(IPAddress.Loopback);
        // A participant already in the session, so a leak of the participant list would show.
        await host.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = "Already Here", InviteCode = session.InviteCode });

        var (_, body, _) = await JoinInfoAsync(session.Id);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(["joinable", "name"], json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain(session.InviteCode!, body);
        Assert.DoesNotContain("Already Here", body);
        Assert.DoesNotContain("Secret Campaign Name", body);
        Assert.DoesNotContain(session.CampaignId.ToString(), body);
        Assert.DoesNotContain(session.Id.ToString(), body);
        Assert.DoesNotContain("LocalLan", body);
    }

    [Fact]
    public async Task Response_IsNotCached()
    {
        var session = await StartSessionAsync();

        var (_, _, response) = await JoinInfoAsync(session.Id);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    // ── Indistinguishable when it cannot be joined ────────────────────────────

    [Fact]
    public async Task UnknownSession_ReturnsNotJoinable_WithStatus200()
    {
        var (status, body, _) = await JoinInfoAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(NotJoinableBody, body);
    }

    [Fact]
    public async Task EndedSession_AnswersExactlyLikeAnUnknownOne()
    {
        var session = await StartSessionAsync(name: "Ended Table");
        using (var host = _factory.CreateClientFrom(IPAddress.Loopback))
            (await host.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null })).EnsureSuccessStatusCode();

        var ended = await JoinInfoAsync(session.Id);
        var unknown = await JoinInfoAsync(Guid.NewGuid());

        Assert.Equal(unknown.Status, ended.Status);
        Assert.Equal(unknown.Body, ended.Body);
        Assert.DoesNotContain("Ended Table", ended.Body);
    }

    [Fact]
    public async Task SoloPlaySession_AnswersExactlyLikeAnUnknownOne()
    {
        // A solo run's play session is named after the adventure; players never join it.
        var session = await StartSessionAsync(SessionAccessMode.Solo, name: "Solo: The Sunken Archive");

        var solo = await JoinInfoAsync(session.Id);

        Assert.Equal(HttpStatusCode.OK, solo.Status);
        Assert.Equal(NotJoinableBody, solo.Body);
    }

    [Theory]
    [InlineData(SessionAccessMode.LocalLan)]
    [InlineData(SessionAccessMode.InternetTunnel)]
    [InlineData(SessionAccessMode.Cloud)]
    public async Task EveryPlayerFacingAccessMode_IsJoinable(SessionAccessMode mode)
    {
        var session = await StartSessionAsync(mode);

        Assert.Contains("\"joinable\":true", (await JoinInfoAsync(session.Id)).Body);
    }

    // Reading the join info must not weaken joining: the invite code is still what lets a player in.
    [Fact]
    public async Task JoinInfo_DoesNotLetAPlayerJoinWithoutTheInviteCode()
    {
        var session = await StartSessionAsync();
        await JoinInfoAsync(session.Id);
        using var lan = _factory.CreateClientFrom(LanAddress);

        var response = await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = "Guesser", InviteCode = "WRONG-CODE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Rate limiting (the same join policy as POST /join: internet mode only) ─

    [Fact]
    public async Task JoinInfo_InInternetMode_IsRateLimited()
    {
        using var factory = new DesktopModeWebApplicationFactory();
        Guid sessionId;
        using (var host = factory.CreateClientFrom(IPAddress.Loopback))
        {
            var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
                new CreateCampaignRequest("C", "D&D 5e 2014", "PHB"))).Content.ReadFromJsonAsync<CampaignDto>();
            var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
                new { Name = "T", AccessMode = (int)SessionAccessMode.InternetTunnel, RequireHostApproval = false }))
                .Content.ReadFromJsonAsync<SessionDto>();
            sessionId = session!.Id;
        }

        factory.Services.GetRequiredService<ITunnelStateService>().SetTunnelActive("https://test-tunnel.trycloudflare.com");
        using var remote = factory.CreateClientFrom(IPAddress.Loopback);
        remote.DefaultRequestHeaders.Host = "test-tunnel.trycloudflare.com";
        remote.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.7");

        var saw429 = false;
        for (var i = 0; i < 14 && !saw429; i++)
        {
            using var response = await remote.GetAsync($"/api/sessions/{sessionId}/join-info");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                saw429 = true;
                Assert.True(response.Headers.Contains("Retry-After"), "A 429 should carry Retry-After.");
            }
        }

        Assert.True(saw429, "Expected a 429 within 14 rapid join-info requests in internet mode.");
    }

    [Fact]
    public async Task JoinInfo_OnTheLan_IsNotRateLimited()
    {
        var session = await StartSessionAsync();

        for (var i = 0; i < 25; i++)
            Assert.Equal(HttpStatusCode.OK, (await JoinInfoAsync(session.Id)).Status);
    }
}
