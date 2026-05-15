using System.Net;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the dice rolling workflow.
/// Verifies: Roll dice → verify roll recorded in session log.
/// </summary>
public class DiceIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DiceIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RollDice_RecordsRollInSession()
    {
        // Arrange - create campaign, session, and join to get a participant ID
        var (sessionId, participantId) = await CreateSessionAndJoinAsync();

        // Act - roll dice
        var rollBody = new
        {
            RollerParticipantId = participantId,
            Formula = "1d20+5",
            Visibility = (int)RollVisibility.Public
        };

        var rollResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/rolls", rollBody);

        // Assert
        Assert.Equal(HttpStatusCode.Created, rollResponse.StatusCode);

        var roll = await rollResponse.Content.ReadFromJsonAsync<RollDto>();
        Assert.NotNull(roll);
        Assert.Equal("1d20+5", roll.Formula);
        Assert.Equal(participantId, roll.RollerParticipantId);
        Assert.Equal(sessionId, roll.SessionId);
        Assert.True(roll.Total >= 6 && roll.Total <= 25); // 1d20 (1-20) + 5
        Assert.Single(roll.DieResults); // 1d20 = one die
    }

    [Fact]
    public async Task RollDice_AppearsInRollLog()
    {
        // Arrange
        var (sessionId, participantId) = await CreateSessionAndJoinAsync();

        var rollBody = new
        {
            RollerParticipantId = participantId,
            Formula = "2d6+3",
            Visibility = (int)RollVisibility.Public
        };

        // Act - roll and then check the log
        var rollResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/rolls", rollBody);
        Assert.Equal(HttpStatusCode.Created, rollResponse.StatusCode);

        var logResponse = await _client.GetAsync($"/api/sessions/{sessionId}/rolls");
        Assert.Equal(HttpStatusCode.OK, logResponse.StatusCode);

        var rolls = await logResponse.Content.ReadFromJsonAsync<List<RollDto>>();
        Assert.NotNull(rolls);
        Assert.Contains(rolls, r => r.Formula == "2d6+3");
    }

    private async Task<(Guid SessionId, Guid ParticipantId)> CreateSessionAndJoinAsync()
    {
        var campaignRequest = new CreateCampaignRequest(
            Name: "Dice Test Campaign",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB");

        var campaignResponse = await _client.PostAsJsonAsync("/api/campaigns", campaignRequest);
        var campaign = await campaignResponse.Content.ReadFromJsonAsync<CampaignDto>();

        var startBody = new
        {
            Name = "Dice Session",
            AccessMode = (int)SessionAccessMode.LocalLan,
            RequireHostApproval = false
        };

        var sessionResponse = await _client.PostAsJsonAsync(
            $"/api/campaigns/{campaign!.Id}/start-session", startBody);
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionDto>();

        var joinBody = new
        {
            DisplayName = "Dice Roller",
            InviteCode = session!.InviteCode!
        };

        var joinResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/join", joinBody);
        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinSessionResult>();

        return (session.Id, joinResult!.ParticipantId);
    }
}
