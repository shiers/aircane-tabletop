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
    private readonly ILogger<PlayerActionController> _logger;

    public PlayerActionController(
        IPlayerActionService playerActionService,
        ISessionHostingService sessionHostingService,
        ILogger<PlayerActionController> logger)
    {
        _playerActionService = playerActionService;
        _sessionHostingService = sessionHostingService;
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
    [HttpPost("{id:guid}/ai/player-action")]
    [ProducesResponseType(typeof(PlayerActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

        var request = new PlayerActionRequest(
            SessionId: id,
            CampaignId: session.CampaignId,
            CharacterId: body.CharacterId,
            ActionText: body.ActionText.Trim());

        try
        {
            var response = await _playerActionService.ProcessActionAsync(request, cancellationToken);
            return Ok(response);
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
