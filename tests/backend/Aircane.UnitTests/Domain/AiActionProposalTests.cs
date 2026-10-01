using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.Domain;

/// <summary>
/// Unit tests for the <see cref="AiActionProposal"/> domain entity.
/// </summary>
public class AiActionProposalTests
{
    private static AiActionProposal CreateProposal() => new(
        sessionId: Guid.NewGuid(),
        campaignId: Guid.NewGuid(),
        actionType: "MoveScene",
        payloadJson: """{"sceneId":"tavern"}""",
        label: "Move to tavern",
        reason: "Players want to rest");

    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var sessionId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();

        var proposal = new AiActionProposal(
            sessionId: sessionId,
            campaignId: campaignId,
            actionType: "ApplyDamage",
            payloadJson: """{"amount":10}""",
            label: "Fire damage",
            reason: "Fireball hit");

        Assert.NotEqual(Guid.Empty, proposal.Id);
        Assert.Equal(sessionId, proposal.SessionId);
        Assert.Equal(campaignId, proposal.CampaignId);
        Assert.Equal("ApplyDamage", proposal.ActionType);
        Assert.Equal("""{"amount":10}""", proposal.PayloadJson);
        Assert.Equal("Fire damage", proposal.Label);
        Assert.Equal("Fireball hit", proposal.Reason);
        Assert.Equal(AiProposalStatus.Pending, proposal.Status);
        Assert.Null(proposal.ResolvedAt);
        Assert.Null(proposal.ResolvedBy);
        Assert.Null(proposal.RejectionReason);
    }

    [Fact]
    public void Approve_PendingProposal_SetsApprovedStatus()
    {
        var proposal = CreateProposal();

        proposal.Approve("Host");

        Assert.Equal(AiProposalStatus.Approved, proposal.Status);
        Assert.NotNull(proposal.ResolvedAt);
        Assert.Equal("Host", proposal.ResolvedBy);
    }

    [Fact]
    public void Approve_NonPendingProposal_ThrowsInvalidOperation()
    {
        var proposal = CreateProposal();
        proposal.Reject("Host");

        Assert.Throws<InvalidOperationException>(() => proposal.Approve("Host"));
    }

    [Fact]
    public void Reject_PendingProposal_SetsRejectedStatus()
    {
        var proposal = CreateProposal();

        proposal.Reject("Host", "Not appropriate");

        Assert.Equal(AiProposalStatus.Rejected, proposal.Status);
        Assert.NotNull(proposal.ResolvedAt);
        Assert.Equal("Host", proposal.ResolvedBy);
        Assert.Equal("Not appropriate", proposal.RejectionReason);
    }

    [Fact]
    public void Reject_WithoutReason_SetsNullReason()
    {
        var proposal = CreateProposal();

        proposal.Reject("Host");

        Assert.Equal(AiProposalStatus.Rejected, proposal.Status);
        Assert.Null(proposal.RejectionReason);
    }

    [Fact]
    public void Reject_NonPendingProposal_ThrowsInvalidOperation()
    {
        var proposal = CreateProposal();
        proposal.Approve("Host");

        Assert.Throws<InvalidOperationException>(() => proposal.Reject("Host"));
    }

    [Fact]
    public void MarkApplied_ApprovedProposal_SetsAppliedStatus()
    {
        var proposal = CreateProposal();
        proposal.Approve("Host");

        proposal.MarkApplied();

        Assert.Equal(AiProposalStatus.Applied, proposal.Status);
    }

    [Fact]
    public void MarkApplied_PendingProposal_ThrowsInvalidOperation()
    {
        var proposal = CreateProposal();

        Assert.Throws<InvalidOperationException>(() => proposal.MarkApplied());
    }

    [Fact]
    public void MarkApplied_RejectedProposal_ThrowsInvalidOperation()
    {
        var proposal = CreateProposal();
        proposal.Reject("Host");

        Assert.Throws<InvalidOperationException>(() => proposal.MarkApplied());
    }
}
