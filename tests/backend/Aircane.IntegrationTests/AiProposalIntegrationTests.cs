using System.Net;
using System.Net.Http.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the AI proposal approval workflow.
/// Verifies: Create proposal → approve → verify state updated.
/// </summary>
public class AiProposalIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly AircaneWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiProposalIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateProposal_Approve_VerifyStatusApplied()
    {
        // Arrange - create campaign and session
        var (campaignId, sessionId) = await CreateCampaignAndSessionAsync();

        // Create a proposal directly via the service (simulating AI output)
        Guid proposalId;
        using (var scope = _factory.Services.CreateScope())
        {
            var proposalService = scope.ServiceProvider.GetRequiredService<IAiProposalService>();

            var proposedAction = new AiProposedAction
            {
                Type = AiActionType.Narrate,
                Label = "Narrate scene introduction",
                Reason = "Player entered the tavern"
            };

            var createRequest = new CreateProposalRequest(
                SessionId: sessionId,
                CampaignId: campaignId,
                ProposedAction: proposedAction);

            var proposal = await proposalService.CreateProposalAsync(createRequest);
            proposalId = proposal.Id;

            Assert.Equal(AiProposalStatus.Pending, proposal.Status);
        }

        // Act - approve the proposal via the API
        var approveResponse = await _client.PostAsync(
            $"/api/sessions/{sessionId}/ai/proposals/{proposalId}/approve", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var approved = await approveResponse.Content.ReadFromJsonAsync<AiProposalDto>();
        Assert.NotNull(approved);
        Assert.Equal(AiProposalStatus.Applied, approved.Status);
        Assert.NotNull(approved.ResolvedAt);
        Assert.Equal("Host", approved.ResolvedBy);
    }

    [Fact]
    public async Task GetPendingProposals_ReturnsOnlyPending()
    {
        // Arrange - create campaign and session
        var (campaignId, sessionId) = await CreateCampaignAndSessionAsync();

        // Create two proposals - approve one, leave one pending
        using (var scope = _factory.Services.CreateScope())
        {
            var proposalService = scope.ServiceProvider.GetRequiredService<IAiProposalService>();

            var action1 = new AiProposedAction
            {
                Type = AiActionType.RequestRoll,
                Label = "Stealth check",
                Formula = "1d20+5",
                Dc = 14,
                Reason = "Sneaking past guards"
            };

            var action2 = new AiProposedAction
            {
                Type = AiActionType.Narrate,
                Label = "Describe the room",
                Reason = "Player looks around"
            };

            var proposal1 = await proposalService.CreateProposalAsync(
                new CreateProposalRequest(sessionId, campaignId, action1));
            await proposalService.CreateProposalAsync(
                new CreateProposalRequest(sessionId, campaignId, action2));

            // Approve the first one
            await proposalService.ApproveProposalAsync(proposal1.Id);
        }

        // Act - get pending proposals
        var response = await _client.GetAsync(
            $"/api/sessions/{sessionId}/ai/proposals");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var proposals = await response.Content.ReadFromJsonAsync<List<AiProposalDto>>();
        Assert.NotNull(proposals);
        Assert.Single(proposals); // Only the second one should be pending
        Assert.Equal("Describe the room", proposals[0].Label);
        Assert.Equal(AiProposalStatus.Pending, proposals[0].Status);
    }

    [Fact]
    public async Task RejectProposal_SetsRejectedStatus()
    {
        // Arrange
        var (campaignId, sessionId) = await CreateCampaignAndSessionAsync();

        Guid proposalId;
        using (var scope = _factory.Services.CreateScope())
        {
            var proposalService = scope.ServiceProvider.GetRequiredService<IAiProposalService>();

            var action = new AiProposedAction
            {
                Type = AiActionType.ApplyDamage,
                Label = "Apply 10 damage to fighter",
                Amount = 10,
                Reason = "Goblin attack"
            };

            var proposal = await proposalService.CreateProposalAsync(
                new CreateProposalRequest(sessionId, campaignId, action));
            proposalId = proposal.Id;
        }

        // Act - reject the proposal
        var rejectBody = new { Reason = "Too much damage for this encounter" };
        var rejectResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/ai/proposals/{proposalId}/reject", rejectBody);

        // Assert
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var rejected = await rejectResponse.Content.ReadFromJsonAsync<AiProposalDto>();
        Assert.NotNull(rejected);
        Assert.Equal(AiProposalStatus.Rejected, rejected.Status);
        Assert.Equal("Too much damage for this encounter", rejected.RejectionReason);
    }

    private async Task<(Guid CampaignId, Guid SessionId)> CreateCampaignAndSessionAsync()
    {
        var campaignRequest = new CreateCampaignRequest(
            Name: "AI Proposal Test Campaign",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB",
            AiRole: AiRole.FullDm,
            AiAuthority: AiAuthority.AskBeforeApplying);

        var campaignResponse = await _client.PostAsJsonAsync("/api/campaigns", campaignRequest);
        var campaign = await campaignResponse.Content.ReadFromJsonAsync<CampaignDto>();

        var startBody = new
        {
            Name = "AI Session",
            AccessMode = (int)SessionAccessMode.LocalLan,
            RequireHostApproval = false
        };

        var sessionResponse = await _client.PostAsJsonAsync(
            $"/api/campaigns/{campaign!.Id}/start-session", startBody);
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionDto>();

        return (campaign.Id, session!.Id);
    }
}
