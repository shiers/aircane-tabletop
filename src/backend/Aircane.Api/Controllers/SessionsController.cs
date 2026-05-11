using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Session management — create sessions under a campaign and retrieve session state.
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public sealed class SessionsController : ControllerBase
{
    private readonly ISessionHostingService _sessions;
    private readonly IParticipantTokenService _tokenService;
    private readonly IValidator<CreateSessionRequest> _createValidator;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionHostingService sessions,
        IParticipantTokenService tokenService,
        IValidator<CreateSessionRequest> createValidator,
        ILogger<SessionsController> logger)
    {
        _sessions = sessions;
        _tokenService = tokenService;
        _createValidator = createValidator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new session under the specified campaign.
    /// Returns the session details including the plain-text invite code (returned once only).
    /// </summary>
    [HttpPost("api/campaigns/{campaignId:guid}/start-session")]
    [ProducesResponseType(typeof(SessionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartSession(
        Guid campaignId,
        [FromBody] StartSessionBody body,
        CancellationToken cancellationToken)
    {
        var request = new CreateSessionRequest(
            CampaignId: campaignId,
            Name: body.Name,
            AccessMode: body.AccessMode,
            RequireHostApproval: body.RequireHostApproval);

        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)),
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var (dto, _) = await _sessions.CreateSessionAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetSession), new { id = dto.Id }, dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Processes a player join request using a display name and invite code.
    /// Returns the participant identity and a signed participant token.
    /// The participant may be pending host approval.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/join")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(JoinSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> JoinSession(
        Guid id,
        [FromBody] JoinSessionBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Display name is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (string.IsNullOrWhiteSpace(body.InviteCode))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Invite code is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var request = new JoinSessionRequest(
                SessionId: id,
                DisplayName: body.DisplayName.Trim(),
                InviteCode: body.InviteCode.Trim());

            var result = await _sessions.JoinSessionAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid invite code",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Cannot join session",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Reconnects a participant using a valid existing token.
    /// Restores the participant's session identity and role without requiring re-approval.
    /// The token must be passed as a Bearer header: Authorization: Bearer {token}
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/reconnect")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(ReconnectResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reconnect(
        Guid id,
        CancellationToken cancellationToken)
    {
        // Extract the Bearer token from the Authorization header.
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Missing token",
                Detail = "A Bearer token is required to reconnect.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var claims = _tokenService.ValidateToken(token);

        if (claims is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid or expired token",
                Detail = "The participant token is invalid, expired, or the session has ended.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        // Ensure the token's session matches the route parameter.
        if (claims.SessionId != id)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Token session mismatch",
                Detail = "The token does not belong to this session.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        var session = await _sessions.GetSessionAsync(id, cancellationToken);
        if (session is null)
            return NotFound();

        _logger.LogInformation(
            "Participant {ParticipantId} reconnected to session {SessionId}",
            claims.ParticipantId, claims.SessionId);

        return Ok(new ReconnectResult(
            SessionId: claims.SessionId,
            ParticipantId: claims.ParticipantId,
            DisplayName: claims.DisplayName,
            Role: claims.Role));
    }

    /// <summary>
    /// Returns the session state and participant count.
    /// The invite code is not included in GET responses.
    /// </summary>
    [HttpGet("api/sessions/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(SessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _sessions.GetSessionAsync(id, cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Ends the session, saves an optional summary, and invalidates all participant tokens.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/end")]
    [ProducesResponseType(typeof(EndSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSession(
        Guid id,
        [FromBody] EndSessionBody? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _sessions.EndSessionAsync(id, body?.Summary, cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Cannot end session",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Returns all participants in the session.
    /// </summary>
    [HttpGet("api/sessions/{id:guid}/participants")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<ParticipantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetParticipants(Guid id, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetSessionAsync(id, cancellationToken);
        if (session is null)
            return NotFound();

        var participants = await _sessions.GetParticipantsAsync(id, cancellationToken);
        return Ok(participants);
    }

    /// <summary>
    /// Approves a pending participant, granting them access to the session.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/approve-participant")]
    [ProducesResponseType(typeof(ParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveParticipant(
        Guid id,
        [FromBody] ApproveParticipantBody body,
        CancellationToken cancellationToken)
    {
        if (body.ParticipantId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "ParticipantId is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var dto = await _sessions.ApproveParticipantAsync(id, body.ParticipantId, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Assigns a character to a participant in the session.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/assign-character")]
    [ProducesResponseType(typeof(ParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignCharacter(
        Guid id,
        [FromBody] AssignCharacterBody body,
        CancellationToken cancellationToken)
    {
        if (body.ParticipantId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "ParticipantId is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (body.CharacterId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "CharacterId is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var dto = await _sessions.AssignCharacterAsync(id, body.ParticipantId, body.CharacterId, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Restores the AI to its prior role and authority after a pause.
    /// The role and authority are read from the campaign configuration.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/resume-ai")]
    [ProducesResponseType(typeof(ResumeAiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResumeAi(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _sessions.ResumeAiAsync(id, cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Cannot resume AI",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }
}

/// <summary>
/// Request body for starting a session. The campaign ID comes from the route.
/// </summary>
public sealed record StartSessionBody(
    string Name,
    Aircane.Domain.Enums.SessionAccessMode AccessMode,
    bool RequireHostApproval = true);

/// <summary>
/// Optional request body for ending a session.
/// </summary>
public sealed record EndSessionBody(
    /// <summary>
    /// Optional narrative summary of the session (e.g. what happened, unresolved threads).
    /// </summary>
    string? Summary = null);

/// <summary>
/// Request body for joining a session. The session ID comes from the route.
/// </summary>
public sealed record JoinSessionBody(
    string DisplayName,
    string InviteCode);

/// <summary>
/// Request body for approving a pending participant.
/// </summary>
public sealed record ApproveParticipantBody(
    Guid ParticipantId);

/// <summary>
/// Request body for assigning a character to a participant.
/// </summary>
public sealed record AssignCharacterBody(
    Guid ParticipantId,
    Guid CharacterId);

/// <summary>
/// Result returned after a successful reconnect using a valid participant token.
/// </summary>
public sealed record ReconnectResult(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    string Role);
