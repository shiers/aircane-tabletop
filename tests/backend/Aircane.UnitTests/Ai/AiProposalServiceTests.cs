using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="AiProposalService"/> using an in-memory EF Core database.
/// </summary>
public class AiProposalServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly AiProposalService _sut;
    private readonly CampaignStateService _stateService;

    public AiProposalServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _stateService = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
        _sut = new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(Campaign Campaign, Session Session)> SeedCampaignAndSessionAsync()
    {
        var campaign = new Campaign("Test Campaign", "D&D 5e", "2014");
        _db.Campaigns.Add(campaign);

        var session = new Session(
            campaignId: campaign.Id,
            name: "Test Session",
            accessMode: SessionAccessMode.LocalLan,
            inviteCodeHash: "test-hash");
        _db.Sessions.Add(session);

        await _db.SaveChangesAsync();
        return (campaign, session);
    }

    private static AiProposedAction CreateSampleAction(
        AiActionType type = AiActionType.MoveScene,
        string? label = "Move to tavern",
        string? reason = "Players want to rest")
    {
        return new AiProposedAction
        {
            Type = type,
            Label = label,
            Reason = reason,
            TargetSceneId = Guid.NewGuid()
        };
    }

    // ── CreateProposalAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateProposalAsync_ValidRequest_CreatesProposal()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction();

        var request = new CreateProposalRequest(session.Id, campaign.Id, action);
        var result = await _sut.CreateProposalAsync(request);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal(campaign.Id, result.CampaignId);
        Assert.Equal("MoveScene", result.ActionType);
        Assert.Equal("Move to tavern", result.Label);
        Assert.Equal("Players want to rest", result.Reason);
        Assert.Equal(AiProposalStatus.Pending, result.Status);
        Assert.Null(result.ResolvedAt);
        Assert.Null(result.ResolvedBy);
    }

    [Fact]
    public async Task CreateProposalAsync_StoresPayloadAsJson()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction(AiActionType.ApplyDamage);

        var request = new CreateProposalRequest(session.Id, campaign.Id, action);
        var result = await _sut.CreateProposalAsync(request);

        // Verify the payload is valid JSON containing the action type (enum serialized as int)
        var payload = JsonSerializer.Deserialize<JsonElement>(result.PayloadJson);
        Assert.True(payload.TryGetProperty("Type", out var typeElement));
        Assert.Equal((int)AiActionType.ApplyDamage, typeElement.GetInt32());
    }

    [Fact]
    public async Task CreateProposalAsync_PersistsToDatabase()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction();

        var request = new CreateProposalRequest(session.Id, campaign.Id, action);
        var result = await _sut.CreateProposalAsync(request);

        var stored = await _db.AiActionProposals.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.Equal(AiProposalStatus.Pending, stored.Status);
    }

    // ── GetPendingProposalsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetPendingProposalsAsync_ReturnsOnlyPending()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        // Create 3 proposals
        for (int i = 0; i < 3; i++)
        {
            var action = CreateSampleAction(label: $"Action {i}");
            await _sut.CreateProposalAsync(new CreateProposalRequest(session.Id, campaign.Id, action));
        }

        // Reject one
        var pending = await _sut.GetPendingProposalsAsync(session.Id);
        await _sut.RejectProposalAsync(pending[0].Id);

        // Should now return 2
        var result = await _sut.GetPendingProposalsAsync(session.Id);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetPendingProposalsAsync_EmptySession_ReturnsEmptyList()
    {
        var result = await _sut.GetPendingProposalsAsync(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingProposalsAsync_OrderedByCreatedAt()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        var action1 = CreateSampleAction(label: "First");
        var action2 = CreateSampleAction(label: "Second");

        await _sut.CreateProposalAsync(new CreateProposalRequest(session.Id, campaign.Id, action1));
        await _sut.CreateProposalAsync(new CreateProposalRequest(session.Id, campaign.Id, action2));

        var result = await _sut.GetPendingProposalsAsync(session.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal("First", result[0].Label);
        Assert.Equal("Second", result[1].Label);
    }

    // ── ApproveProposalAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ApproveProposalAsync_ValidProposal_SetsAppliedStatus()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction();

        var created = await _sut.CreateProposalAsync(
            new CreateProposalRequest(session.Id, campaign.Id, action));

        var result = await _sut.ApproveProposalAsync(created.Id);

        Assert.Equal(AiProposalStatus.Applied, result.Status);
        Assert.NotNull(result.ResolvedAt);
        Assert.Equal("Host", result.ResolvedBy);
    }

    [Fact]
    public async Task ApproveProposalAsync_AppliesCommandToCampaignState()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        // Create a MoveScene proposal with proper payload structure
        var proposal = new AiActionProposal(
            sessionId: session.Id,
            campaignId: campaign.Id,
            actionType: "MoveScene",
            payloadJson: """{"sceneId":"tavern-entrance"}""",
            label: "Move to tavern",
            reason: "Players want to rest");
        _db.AiActionProposals.Add(proposal);
        await _db.SaveChangesAsync();

        await _sut.ApproveProposalAsync(proposal.Id);

        // Verify state was updated
        var state = await _stateService.LoadStateAsync(campaign.Id);
        Assert.Equal("tavern-entrance", state.CurrentSceneJson);
    }

    [Fact]
    public async Task ApproveProposalAsync_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.ApproveProposalAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ApproveProposalAsync_AlreadyRejected_ThrowsInvalidOperation()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction();

        var created = await _sut.CreateProposalAsync(
            new CreateProposalRequest(session.Id, campaign.Id, action));
        await _sut.RejectProposalAsync(created.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ApproveProposalAsync(created.Id));
    }

    // ── RejectProposalAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RejectProposalAsync_ValidProposal_SetsRejectedStatus()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction();

        var created = await _sut.CreateProposalAsync(
            new CreateProposalRequest(session.Id, campaign.Id, action));

        var result = await _sut.RejectProposalAsync(created.Id, "Host", "Not appropriate");

        Assert.Equal(AiProposalStatus.Rejected, result.Status);
        Assert.NotNull(result.ResolvedAt);
        Assert.Equal("Host", result.ResolvedBy);
        Assert.Equal("Not appropriate", result.RejectionReason);
    }

    [Fact]
    public async Task RejectProposalAsync_WithoutReason_SetsNullReason()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var action = CreateSampleAction();

        var created = await _sut.CreateProposalAsync(
            new CreateProposalRequest(session.Id, campaign.Id, action));

        var result = await _sut.RejectProposalAsync(created.Id);

        Assert.Equal(AiProposalStatus.Rejected, result.Status);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public async Task RejectProposalAsync_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.RejectProposalAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RejectProposalAsync_AlreadyApproved_ThrowsInvalidOperation()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        // Create a proposal with raw payload (bypassing structured action serialization)
        var proposal = new AiActionProposal(
            sessionId: session.Id,
            campaignId: campaign.Id,
            actionType: "MoveScene",
            payloadJson: """{"sceneId":"forest"}""",
            label: "Move to forest");
        _db.AiActionProposals.Add(proposal);
        await _db.SaveChangesAsync();

        await _sut.ApproveProposalAsync(proposal.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RejectProposalAsync(proposal.Id));
    }

    // ── Integration: full workflow ────────────────────────────────────────────

    [Fact]
    public async Task FullWorkflow_CreateListApproveVerify()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        // Create multiple proposals
        var action1 = CreateSampleAction(AiActionType.RequestRoll, "Stealth check", "Sneaking past guards");
        var action2 = CreateSampleAction(AiActionType.ApplyDamage, "Fire damage", "Fireball hit");

        await _sut.CreateProposalAsync(new CreateProposalRequest(session.Id, campaign.Id, action1));
        await _sut.CreateProposalAsync(new CreateProposalRequest(session.Id, campaign.Id, action2));

        // List pending - should have 2
        var pending = await _sut.GetPendingProposalsAsync(session.Id);
        Assert.Equal(2, pending.Count);

        // Approve first, reject second
        await _sut.ApproveProposalAsync(pending[0].Id);
        await _sut.RejectProposalAsync(pending[1].Id, "Host", "Too much damage");

        // List pending - should be empty
        var remaining = await _sut.GetPendingProposalsAsync(session.Id);
        Assert.Empty(remaining);
    }
}
