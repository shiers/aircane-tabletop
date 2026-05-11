using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// EF Core-backed implementation of <see cref="IAiProposalService"/>.
/// Manages the AI action proposal queue and applies approved actions to campaign state.
/// </summary>
public sealed class AiProposalService : IAiProposalService
{
    private readonly AircaneDbContext _db;
    private readonly ICampaignStateService _campaignStateService;
    private readonly ILogger<AiProposalService> _logger;

    public AiProposalService(
        AircaneDbContext db,
        ICampaignStateService campaignStateService,
        ILogger<AiProposalService> logger)
    {
        _db = db;
        _campaignStateService = campaignStateService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AiProposalDto> CreateProposalAsync(
        CreateProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ProposedAction);

        var payloadJson = JsonSerializer.Serialize(request.ProposedAction);

        var proposal = new AiActionProposal(
            sessionId: request.SessionId,
            campaignId: request.CampaignId,
            actionType: request.ProposedAction.Type.ToString(),
            payloadJson: payloadJson,
            label: request.ProposedAction.Label,
            reason: request.ProposedAction.Reason);

        _db.AiActionProposals.Add(proposal);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created AI proposal {ProposalId} of type '{ActionType}' for session {SessionId}",
            proposal.Id, proposal.ActionType, proposal.SessionId);

        return ToDto(proposal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiProposalDto>> GetPendingProposalsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var proposals = await _db.AiActionProposals
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.Status == AiProposalStatus.Pending)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return proposals.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<AiProposalDto> ApproveProposalAsync(
        Guid proposalId,
        string approvedBy = "Host",
        CancellationToken cancellationToken = default)
    {
        var proposal = await _db.AiActionProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

        proposal.Approve(approvedBy);

        // Apply the action to campaign state
        var commandRequest = new ApplyCommandRequest(
            CampaignId: proposal.CampaignId,
            CommandType: proposal.ActionType,
            PayloadJson: proposal.PayloadJson,
            ActorType: "AI",
            SessionId: proposal.SessionId);

        await _campaignStateService.ApplyCommandAsync(commandRequest, cancellationToken);

        proposal.MarkApplied();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Approved and applied AI proposal {ProposalId} (type: {ActionType}) for campaign {CampaignId}",
            proposal.Id, proposal.ActionType, proposal.CampaignId);

        return ToDto(proposal);
    }

    /// <inheritdoc />
    public async Task<AiProposalDto> RejectProposalAsync(
        Guid proposalId,
        string rejectedBy = "Host",
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _db.AiActionProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

        proposal.Reject(rejectedBy, reason);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rejected AI proposal {ProposalId} (type: {ActionType}) for session {SessionId}. Reason: {Reason}",
            proposal.Id, proposal.ActionType, proposal.SessionId, reason ?? "(none)");

        return ToDto(proposal);
    }

    private static AiProposalDto ToDto(AiActionProposal p) => new(
        Id: p.Id,
        SessionId: p.SessionId,
        CampaignId: p.CampaignId,
        ActionType: p.ActionType,
        PayloadJson: p.PayloadJson,
        Label: p.Label,
        Reason: p.Reason,
        Status: p.Status,
        CreatedAt: p.CreatedAt,
        ResolvedAt: p.ResolvedAt,
        ResolvedBy: p.ResolvedBy,
        RejectionReason: p.RejectionReason);
}
