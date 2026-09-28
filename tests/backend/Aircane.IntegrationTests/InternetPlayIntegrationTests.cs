using System.Net;
using System.Net.Http.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the internet-play hardening (rate limiting, CSRF, persistent token
/// revocation) and the tunnel-aware network-info endpoint.
/// <para>
/// Each test uses its own factory so the process-wide internet-mode flag and the per-IP rate
/// limiter never leak between tests. Internet mode is toggled by resolving the singleton
/// <see cref="ITunnelStateService"/> from the test host (the same object the running app uses).
/// </para>
/// </summary>
public class InternetPlayIntegrationTests
{
    private const string TunnelOrigin = "https://test-tunnel.trycloudflare.com";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void EnableInternetMode(AircaneWebApplicationFactory factory)
    {
        var tunnel = factory.Services.GetRequiredService<ITunnelStateService>();
        tunnel.SetTunnelActive(TunnelOrigin);
    }

    private static async Task<(Guid campaignId, SessionDto session)> CreateActiveSessionAsync(
        HttpClient client)
    {
        var campaignRequest = new CreateCampaignRequest(
            Name: "Internet Play Campaign",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB",
            AiRole: AiRole.Assistant,
            AiAuthority: AiAuthority.SuggestOnly);

        var campaignResponse = await client.PostAsJsonAsync("/api/campaigns", campaignRequest);
        campaignResponse.EnsureSuccessStatusCode();
        var campaign = await campaignResponse.Content.ReadFromJsonAsync<CampaignDto>();
        Assert.NotNull(campaign);

        var startBody = new
        {
            Name = "Internet Session",
            AccessMode = (int)SessionAccessMode.InternetTunnel,
            RequireHostApproval = false,
        };
        var sessionResponse = await client.PostAsJsonAsync(
            $"/api/campaigns/{campaign!.Id}/start-session", startBody);
        sessionResponse.EnsureSuccessStatusCode();
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionDto>();
        Assert.NotNull(session);

        return (campaign.Id, session!);
    }

    /// <summary>
    /// Builds a join request that satisfies the internet-mode CSRF checks (json content type is
    /// set by JsonContent; here we add the trusted Origin and the X-Requested-With header).
    /// </summary>
    private static HttpRequestMessage BuildJoinRequest(Guid sessionId, string displayName, string inviteCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/join")
        {
            Content = JsonContent.Create(new { DisplayName = displayName, InviteCode = inviteCode }),
        };
        request.Headers.Add("Origin", TunnelOrigin);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return request;
    }

    // ── Rate limiting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinEndpoint_ExceedsRateLimit_Returns429()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        // Create the session in LAN mode (setup POSTs are not subject to CSRF), then switch
        // the process into internet mode so the limiter engages for the join requests.
        var (_, session) = await CreateActiveSessionAsync(client);
        EnableInternetMode(factory);

        // The join fixed-window limit is 10/min. The first 10 requests are permitted (they use a
        // wrong code, so they return 400, but they still count against the limit); the 11th is
        // shed by the limiter with 429 before reaching the handler.
        HttpStatusCode? lastStatus = null;
        var saw429 = false;
        for (var i = 0; i < 12; i++)
        {
            using var request = BuildJoinRequest(session.Id, "Spammer", "WRONG-CODE");
            using var response = await client.SendAsync(request);
            lastStatus = response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                saw429 = true;
                Assert.True(
                    response.Headers.Contains("Retry-After"),
                    "429 response should include a Retry-After header.");
                break;
            }
        }

        Assert.True(saw429, $"Expected a 429 within 12 rapid requests; last status was {lastStatus}.");
    }

    [Fact]
    public async Task JoinEndpoint_LanSession_NoRateLimit()
    {
        // No tunnel => LAN mode => limiter is a no-op. 12 rapid joins should never see a 429.
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        var (_, session) = await CreateActiveSessionAsync(client);

        for (var i = 0; i < 12; i++)
        {
            var joinBody = new { DisplayName = "LanPlayer", InviteCode = "WRONG-CODE" };
            using var response = await client.PostAsJsonAsync(
                $"/api/sessions/{session.Id}/join", joinBody);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    // ── CSRF ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StateEndpoint_ForeignOrigin_Rejected()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        var (_, session) = await CreateActiveSessionAsync(client);
        EnableInternetMode(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{session.Id}/join")
        {
            Content = JsonContent.Create(new { DisplayName = "Attacker", InviteCode = session.InviteCode }),
        };
        request.Headers.Add("Origin", "https://evil.com");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StateEndpoint_TunnelOrigin_Accepted()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        var (_, session) = await CreateActiveSessionAsync(client);
        EnableInternetMode(factory);

        using var request = BuildJoinRequest(session.Id, "RemotePlayer", session.InviteCode!);
        using var response = await client.SendAsync(request);

        // Passes the CSRF gate and reaches the handler -> a successful join.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StateEndpoint_Localhost_Accepted()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        var (_, session) = await CreateActiveSessionAsync(client);
        EnableInternetMode(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{session.Id}/join")
        {
            Content = JsonContent.Create(new { DisplayName = "LocalPlayer", InviteCode = session.InviteCode }),
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Token revocation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EndSession_RevokesAllTokens_InDatabaseAndMemory()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        var (_, session) = await CreateActiveSessionAsync(client);

        // Join so there is an active participant token to revoke.
        var joinBody = new { DisplayName = "Player", InviteCode = session.InviteCode };
        var joinResponse = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/join", joinBody);
        joinResponse.EnsureSuccessStatusCode();

        // End the session.
        var endResponse = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/end", new { Summary = (string?)null });
        endResponse.EnsureSuccessStatusCode();

        // In-memory fast path: the session is revoked.
        var revocation = factory.Services.GetRequiredService<ITokenRevocationService>();
        Assert.True(revocation.IsSessionRevoked(session.Id));

        // Persistent store: at least one RevokedTokens row exists for the session.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var revokedCount = await db.RevokedTokens.CountAsync(r => r.SessionId == session.Id);
        Assert.True(revokedCount >= 1, "Ending the session should persist at least one revoked token.");
    }

    [Fact]
    public async Task RevokedToken_AfterServerRestart_StillRejected()
    {
        var dbName = $"AircaneRestartDb_{Guid.NewGuid()}";

        Guid sessionId;
        string participantToken;

        // ── First "process": create + join + end the session. ──
        using (var factory = new SharedDbWebApplicationFactory(dbName))
        {
            var client = factory.CreateClient();
            var (_, session) = await CreateActiveSessionAsync(client);
            sessionId = session.Id;

            var joinBody = new { DisplayName = "Player", InviteCode = session.InviteCode };
            var joinResponse = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/join", joinBody);
            joinResponse.EnsureSuccessStatusCode();
            var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinSessionResult>();
            participantToken = joinResult!.ParticipantToken;

            var endResponse = await client.PostAsJsonAsync(
                $"/api/sessions/{session.Id}/end", new { Summary = (string?)null });
            endResponse.EnsureSuccessStatusCode();
        }

        // ── Second "process": a fresh factory sharing the same DB. Its in-memory revocation
        //    set is empty (as after a restart), but the persistent RevokedTokens row remains,
        //    so the old token must still be rejected on reconnect. ──
        using (var factory = new SharedDbWebApplicationFactory(dbName))
        {
            var client = factory.CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/reconnect");
            request.Headers.Add("Authorization", $"Bearer {participantToken}");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task TokenCleanupJob_RemovesExpiredRevocations()
    {
        using var factory = new AircaneWebApplicationFactory();

        // Seed an already-expired revocation directly in the store.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            db.RevokedTokens.Add(new RevokedToken(
                tokenId: Guid.NewGuid().ToString("N"),
                sessionId: Guid.NewGuid(),
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)));
            db.RevokedTokens.Add(new RevokedToken(
                tokenId: Guid.NewGuid().ToString("N"),
                sessionId: Guid.NewGuid(),
                expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            await db.SaveChangesAsync();
        }

        var revocation = factory.Services.GetRequiredService<ITokenRevocationService>();
        var removed = await revocation.PurgeExpiredAsync();

        Assert.Equal(1, removed);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            // Only the future-expiry row should remain.
            Assert.Equal(1, await db.RevokedTokens.CountAsync());
            Assert.True(await db.RevokedTokens.AllAsync(r => r.ExpiresAt > DateTimeOffset.UtcNow));
        }
    }

    // ── Network info ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NetworkInfo_TunnelActive_IncludesTunnelUrl()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();
        EnableInternetMode(factory);

        var response = await client.GetAsync("/api/sessions/network-info");
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<NetworkInfoDto>();
        Assert.NotNull(info);
        Assert.True(info!.TunnelActive);
        Assert.Equal(TunnelOrigin, info.TunnelUrl);
    }

    [Fact]
    public async Task NetworkInfo_TunnelInactive_TunnelUrlIsNull()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sessions/network-info");
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<NetworkInfoDto>();
        Assert.NotNull(info);
        Assert.False(info!.TunnelActive);
        Assert.Null(info.TunnelUrl);
    }
}
