using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.CampaignState;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Campaign state and event log endpoints — provides access to the current
/// campaign state snapshot and the paginated event audit log.
/// </summary>
[ApiController]
[Route("api/campaigns/{campaignId:guid}")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class CampaignStateController : ControllerBase
{
    private readonly ICampaignStateService _stateService;
    private readonly ILogger<CampaignStateController> _logger;

    public CampaignStateController(
        ICampaignStateService stateService,
        ILogger<CampaignStateController> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current campaign state snapshot.
    /// </summary>
    [HttpGet("state")]
    [ProducesResponseType(typeof(CampaignStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetState(
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _stateService.LoadStateAsync(campaignId, cancellationToken);
            return Ok(state);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Campaign {CampaignId} not found when loading state", campaignId);
            return NotFound();
        }
    }

    /// <summary>
    /// Returns the paginated event log for a campaign, ordered by most recent first.
    /// </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvents(
        Guid campaignId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        try
        {
            var events = await _stateService.GetEventLogAsync(
                campaignId, page, pageSize, cancellationToken);
            return Ok(events);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Undoes the most recent reversible command and returns the restored state.
    /// </summary>
    [HttpPost("state/undo")]
    [ProducesResponseType(typeof(CampaignStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UndoLastCommand(
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _stateService.UndoLastCommandAsync(campaignId, cancellationToken);
            return Ok(state);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Rebuilds the campaign state by replaying all command events from the event log.
    /// Useful for verification and recovery. Does not modify the stored snapshot.
    /// </summary>
    [HttpGet("state/replay")]
    [ProducesResponseType(typeof(CampaignStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayState(
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _stateService.ReplayEventsAsync(campaignId, cancellationToken);
            return Ok(state);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
