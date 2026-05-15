using System.Net;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the session join workflow.
/// Verifies: Create campaign → start session → join → verify participant.
/// </summary>
public class SessionIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SessionIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateSession_JoinSession_VerifiesParticipant()
    {
        // Arrange - create a campaign first
        var campaignRequest = new CreateCampaignRequest(
            Name: "Test Campaign",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB",
            AiRole: AiRole.Assistant,
            AiAuthority: AiAuthority.SuggestOnly);

        var campaignResponse = await _client.PostAsJsonAsync("/api/campaigns", campaignRequest);
        Assert.Equal(HttpStatusCode.Created, campaignResponse.StatusCode);

        var campaign = await campaignResponse.Content.ReadFromJsonAsync<CampaignDto>();
        Assert.NotNull(campaign);

        // Act - start a session
        var startBody = new
        {
            Name = "Session 1",
            AccessMode = (int)SessionAccessMode.LocalLan,
            RequireHostApproval = false
        };

        var sessionResponse = await _client.PostAsJsonAsync(
            $"/api/campaigns/{campaign.Id}/start-session", startBody);
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionDto>();
        Assert.NotNull(session);
        Assert.NotNull(session.InviteCode);

        // Act - join the session as a player
        var joinBody = new
        {
            DisplayName = "Player One",
            InviteCode = session.InviteCode
        };

        var joinResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/join", joinBody);
        Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);

        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinSessionResult>();
        Assert.NotNull(joinResult);
        Assert.Equal("Player One", joinResult.DisplayName);
        Assert.Equal(ParticipantRole.Player, joinResult.Role);
        Assert.NotEqual(Guid.Empty, joinResult.ParticipantId);
        Assert.False(string.IsNullOrEmpty(joinResult.ParticipantToken));
    }

    [Fact]
    public async Task JoinSession_WithInvalidInviteCode_ReturnsBadRequest()
    {
        // Arrange - create campaign and session
        var campaignRequest = new CreateCampaignRequest(
            Name: "Auth Test Campaign",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB");

        var campaignResponse = await _client.PostAsJsonAsync("/api/campaigns", campaignRequest);
        var campaign = await campaignResponse.Content.ReadFromJsonAsync<CampaignDto>();
        Assert.NotNull(campaign);

        var startBody = new
        {
            Name = "Auth Session",
            AccessMode = (int)SessionAccessMode.LocalLan,
            RequireHostApproval = false
        };

        var sessionResponse = await _client.PostAsJsonAsync(
            $"/api/campaigns/{campaign.Id}/start-session", startBody);
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionDto>();
        Assert.NotNull(session);

        // Act - try to join with wrong invite code
        var joinBody = new
        {
            DisplayName = "Hacker",
            InviteCode = "WRONG-CODE"
        };

        var joinResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/join", joinBody);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, joinResponse.StatusCode);
    }
}
