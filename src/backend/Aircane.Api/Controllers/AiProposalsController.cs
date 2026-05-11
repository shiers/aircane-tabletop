using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// AI proposal queue endpoints — allows the host to list, approve, and reject
/// AI-proposed actions before they are applied to campaign state.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId:guid}/ai/proposals")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class AiProposalsController : ControllerBase
{
    private readonly IAiProposalService _proposalService;
    private readonly ILogger<AiProposalsController> _logger;

    public AiProposalsController(
        IAiProposalService proposalService,
        ILogger<AiProposalsController> logger)
    {
        _proposalService = proposalService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all pending AI proposals for the specified session.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AiProposalDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingProposals(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var proposals = await _proposalService.GetPendingProposalsAsync(sessionId, cancellationToken);
        return Ok(proposals);
    }

    /// <summary>
    /// Approves a pending AI proposal and applies the action to campaign state.
    /// </summary>
    [HttpPost("{proposalId:guid}/approve")]
    [ProducesResponseType(typeof(AiProposalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveProposal(
        Guid sessionId,
        Guid proposalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _proposalService.ApproveProposalAsync(
                proposalId, "Host", cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot approve proposal {ProposalId}", proposalId);
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Rejects a pending AI proposal with an optional reason.
    /// </summary>
    [HttpPost("{proposalId:guid}/reject")]
    [ProducesResponseType(typeof(AiProposalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectProposal(
        Guid sessionId,
        Guid proposalId,
        [FromBody] RejectProposalRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _proposalService.RejectProposalAsync(
                proposalId, "Host", request?.Reason, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot reject proposal {ProposalId}", proposalId);
            return Conflict(new { error = ex.Message });
        }
    }
}
