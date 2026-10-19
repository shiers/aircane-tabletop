using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Handles player action submissions through the AI DM pipeline.
/// </summary>
[ApiController]
[Route("api/sessions")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class PlayerActionController : ControllerBase
{
    private readonly IPlayerActionService _playerActionService;
    private readonly ISessionHostingService _sessionHostingService;
    private readonly IAdventureSessionService _adventureSessions;
    private readonly ILogger<PlayerActionController> _logger;

    public PlayerActionController(
        IPlayerActionService playerActionService,
        ISessionHostingService sessionHostingService,
        IAdventureSessionService adventureSessions,
        ILogger<PlayerActionController> logger)
    {
        _playerActionService = playerActionService;
        _sessionHostingService = sessionHostingService;
        _adventureSessions = adventureSessions;
        _logger = logger;
    }

    /// <summary>
    /// Submits a player action to the AI DM pipeline. The backend loads campaign state,
    /// retrieves relevant context via RAG, prompts the AI, and returns narration plus
    /// the status of any proposed state-changing actions.
    /// </summary>
    /// <param name="id">The session ID.</param>
    /// <param name="body">The player action request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AI DM response with narration and action results.</returns>
    /// <remarks>
    /// Only the Host or an approved participant of this session may call it (403 otherwise); a Player's
    /// response never carries the AI's private DM note.
    /// Not available while the campaign has a group adventure run in progress (409): that run is
    /// played through <c>POST /api/adventure-sessions/{id}/player-action</c>, which enforces whose
    /// turn it is. Solo runs and campaigns with no run are unaffected.
    /// </remarks>
    [HttpPost("{id:guid}/ai/player-action")]
    [ProducesResponseType(typeof(PlayerActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitPlayerAction(
        Guid id,
        [FromBody] PlayerActionBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Request body is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (string.IsNullOrWhiteSpace(body.ActionText))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "ActionText is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (body.CharacterId == Guid.Empty)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "CharacterId is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Verify the session exists and get its campaign ID
        var session = await _sessionHostingService.GetSessionAsync(id, cancellationToken);
        if (session is null)
            return NotFound();

        // A Player must hold an approved seat at THIS session (a token for another session, a participant
        // still waiting for approval, or one the host denied, is refused). The Host needs no seat.
        var isHost = this.IsHostCaller();
        if (!isHost)
        {
            var seat = await PlayerSeat.CheckAsync(User, id, _sessionHostingService, cancellationToken);
            if (seat != SeatStatus.Allowed)
                return PlayerSeat.Refusal(seat);
        }

        // The AI picks up the campaign's in-progress run whichever session id is used, so the check
        // is keyed on the campaign too: otherwise any other session of it would sidestep the guard.
        var run = await _adventureSessions.GetActiveByCampaignAsync(session.CampaignId, cancellationToken);
        if (run is { IsGroup: true })
        {
            return Conflict(new ProblemDetails
            {
                Title = "Group adventure in progress",
                Detail = "This campaign is running a group adventure, which is played through " +
                         $"POST /api/adventure-sessions/{run.Id}/player-action so that only the acting player can act.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        var request = new PlayerActionRequest(
            SessionId: id,
            CampaignId: session.CampaignId,
            CharacterId: body.CharacterId,
            ActionText: body.ActionText.Trim());

        try
        {
            var response = await _playerActionService.ProcessActionAsync(request, cancellationToken);

            // The AI's private DM note is for the Host only.
            return Ok(isHost ? response : response with { PrivateDmNote = null });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found while processing player action for session {SessionId}", id);
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while processing player action for session {SessionId}", id);
            return BadRequest(new ProblemDetails
            {
                Title = "Cannot process action",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }
}

/// <summary>
/// Request body for submitting a player action. The session ID comes from the route.
/// </summary>
public sealed record PlayerActionBody(
    /// <summary>The character performing the action.</summary>
    Guid CharacterId,

    /// <summary>The player's action text (e.g. "I try to sneak past the guards").</summary>
    string ActionText);
