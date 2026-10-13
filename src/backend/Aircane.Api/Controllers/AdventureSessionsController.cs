using System.Security.Claims;
using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Solo adventure execution: start a run of a generated adventure, read its state, play it
/// through the AI DM, and override it manually. The caller must be the Host, or the solo Player
/// who joined the run's play session.
/// </summary>
[ApiController]
[Route("api/adventure-sessions")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class AdventureSessionsController : ControllerBase
{
    // Claim carrying the session a participant token was issued for (see ParticipantTokenService).
    private const string SessionIdClaim = "sid";

    private readonly IAdventureSessionService _adventureSessions;
    private readonly IPlayerActionService _playerActionService;
    private readonly ISessionHostingService _sessionHosting;
    private readonly ILogger<AdventureSessionsController> _logger;

    public AdventureSessionsController(
        IAdventureSessionService adventureSessions,
        IPlayerActionService playerActionService,
        ISessionHostingService sessionHosting,
        ILogger<AdventureSessionsController> logger)
    {
        _adventureSessions = adventureSessions;
        _playerActionService = playerActionService;
        _sessionHosting = sessionHosting;
        _logger = logger;
    }

    /// <summary>
    /// Starts a run of an approved adventure in a campaign, or resumes the campaign's in-progress
    /// run of that adventure. A play session is created on first start so live events have a
    /// SignalR group to go to.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdventureSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartSession(
        [FromBody] StartAdventureSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsHost())
            return Forbid();

        if (request is null || request.GeneratedAdventureId == Guid.Empty || request.CampaignId == Guid.Empty)
            return Problem400("generatedAdventureId and campaignId are required.");

        try
        {
            var active = await _adventureSessions.GetActiveByCampaignAsync(request.CampaignId, cancellationToken);
            if (active is not null && active.GeneratedAdventureId != request.GeneratedAdventureId)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Adventure already in progress",
                    Detail = "This campaign already has an adventure in progress. Abandon or complete it before starting another.",
                    Status = StatusCodes.Status409Conflict,
                });
            }

            var session = await _adventureSessions.StartAdventureSessionAsync(
                request.GeneratedAdventureId, request.CampaignId, request.CharacterId, cancellationToken);

            // First start (or a run created before a play session existed): attach a solo play
            // session so the SignalR events have a group to be delivered to.
            if (session.SessionId is null)
            {
                var state = await _adventureSessions.GetSessionStateAsync(session.Id, cancellationToken);
                var (playSession, _) = await _sessionHosting.CreateSessionAsync(
                    new CreateSessionRequest(
                        CampaignId: session.CampaignId,
                        Name: $"Solo: {state.AdventureTitle}",
                        AccessMode: SessionAccessMode.Solo,
                        RequireHostApproval: false),
                    cancellationToken);
                await _adventureSessions.AttachPlaySessionAsync(session.Id, playSession.Id, cancellationToken);
                session.SessionId = playSession.Id;
            }

            return Ok(ToDto(session));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(NotFoundProblem(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Problem400(ex.Message);
        }
    }

    /// <summary>
    /// Returns the full current state of an adventure run.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return await RunAsync(async () => Ok(await _adventureSessions.GetSessionStateAsync(id, cancellationToken)));
    }

    /// <summary>
    /// Every run that is in progress, most recently active first (Host only). Used by the dashboard
    /// to offer "Resume Adventure".
    /// </summary>
    [HttpGet("in-progress")]
    [ProducesResponseType(typeof(IReadOnlyList<AdventureSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListInProgress(CancellationToken cancellationToken)
    {
        if (!IsHost())
            return Forbid();

        var runs = await _adventureSessions.ListInProgressAsync(cancellationToken);
        return Ok(runs.Select(ToDto).ToList());
    }

    /// <summary>
    /// Marks the run as resumed (a player has come back to it) and clears any stale pending roll.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return await RunAsync(async () =>
        {
            await _adventureSessions.ResumeAsync(id, cancellationToken);
            return NoContent();
        });
    }

    /// <summary>
    /// A 2-3 sentence "story so far" for a returning player, written by the AI from the run's state.
    /// </summary>
    [HttpPost("{id:guid}/recap")]
    [ProducesResponseType(typeof(AdventureRecapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recap(Guid id, CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return await RunAsync(async () =>
            Ok(new AdventureRecapDto(await _adventureSessions.GenerateRecapAsync(id, cancellationToken))));
    }

    /// <summary>
    /// The run's saved conversation: the last 50 entries, oldest first. 50 entries is about 25
    /// exchanges, a full one-shot.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<AdventureHistoryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return Ok(await _adventureSessions.GetHistoryAsync(id, 50, cancellationToken));
    }

    /// <summary>
    /// Returns the campaign's in-progress run (as a lightweight DTO), or 404 if there is none.
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(AdventureSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActiveSession(
        [FromQuery] Guid campaignId,
        CancellationToken cancellationToken)
    {
        if (campaignId == Guid.Empty)
            return Problem400("campaignId is required.");

        var session = await _adventureSessions.GetActiveByCampaignAsync(campaignId, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return Ok(ToDto(session));
    }

    /// <summary>
    /// Submits a free-text player action to the AI DM for this run. Returns the narration, the
    /// outcome of any adventure actions the AI proposed, and the refreshed run state.
    /// </summary>
    [HttpPost("{id:guid}/player-action")]
    [ProducesResponseType(typeof(AdventureSessionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitPlayerAction(
        Guid id,
        [FromBody] AdventureSessionActionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ActionText))
            return Problem400("actionText is required.");

        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();
        if (session.Status != AdventureSessionStatus.InProgress)
            return Problem400($"Adventure session is {session.Status}; it can no longer be played.");

        return await RunAsync(async () =>
        {
            var response = await _playerActionService.ProcessActionAsync(
                new PlayerActionRequest(
                    SessionId: session.SessionId ?? Guid.Empty,
                    CampaignId: session.CampaignId,
                    CharacterId: session.CharacterId ?? Guid.Empty,
                    ActionText: request.ActionText.Trim()),
                cancellationToken);

            var state = await _adventureSessions.GetSessionStateAsync(id, cancellationToken);
            return Ok(new AdventureSessionActionResponse(
                response.Narration,
                IsHost() ? response.PrivateDmNote : null,   // DM-only: never sent to a Player token
                response.ProposedActions.Select(AdventureActionOutcomeDto.From).ToList(),
                state));
        });
    }

    /// <summary>
    /// Manually advances to a scene reachable from the current one (host override).
    /// </summary>
    [HttpPost("{id:guid}/advance-scene")]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdvanceScene(
        Guid id,
        [FromBody] AdvanceSceneRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TargetSceneId))
            return Problem400("targetSceneId is required.");

        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return await RunAsync(async () =>
        {
            await _adventureSessions.AdvanceSceneAsync(
                id, request.TargetSceneId, request.TransitionNarration, cancellationToken);
            return Ok(await _adventureSessions.GetSessionStateAsync(id, cancellationToken));
        });
    }

    /// <summary>
    /// Abandons the run. Idempotent for an already-abandoned run.
    /// </summary>
    [HttpPost("{id:guid}/abandon")]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Abandon(Guid id, CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return await RunAsync(async () =>
        {
            await _adventureSessions.AbandonAdventureAsync(id, cancellationToken);
            return Ok(await _adventureSessions.GetSessionStateAsync(id, cancellationToken));
        });
    }

    /// <summary>
    /// Returns the scenes reachable from the run's current scene.
    /// </summary>
    [HttpGet("{id:guid}/available-scenes")]
    [ProducesResponseType(typeof(IReadOnlyList<AdventureExitDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailableScenes(Guid id, CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (!CanAccess(session))
            return Forbid();

        return await RunAsync(async () => Ok(await _adventureSessions.GetAvailableScenesAsync(id, cancellationToken)));
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// True when the caller is the Host. An unauthenticated caller can only reach this action when
    /// the "Authenticated" policy is permissive (Development / <c>Aircane:TrustLocalHost</c>),
    /// where the single local user is the host.
    /// </summary>
    private bool IsHost()
        => User.Identity?.IsAuthenticated != true
           || string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, nameof(ParticipantRole.Host), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Host, or the solo Player whose token was issued for this run's play session.
    /// </summary>
    private bool CanAccess(AdventureSession session)
    {
        if (IsHost())
            return true;

        var isPlayer = string.Equals(
            User.FindFirst(ClaimTypes.Role)?.Value, nameof(ParticipantRole.Player), StringComparison.OrdinalIgnoreCase);

        return isPlayer
               && session.SessionId is not null
               && Guid.TryParse(User.FindFirst(SessionIdClaim)?.Value, out var tokenSessionId)
               && tokenSessionId == session.SessionId;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Maps the service's exceptions to HTTP problem responses.</summary>
    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(NotFoundProblem(ex));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(ex, "Adventure session request rejected: {Reason}", ex.Message);
            return Problem400(ex.Message);
        }
    }

    private BadRequestObjectResult Problem400(string detail) => BadRequest(new ProblemDetails
    {
        Title = "Cannot process request",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest,
    });

    private static ProblemDetails NotFoundProblem(KeyNotFoundException ex) => new()
    {
        Title = "Not found",
        Detail = ex.Message,
        Status = StatusCodes.Status404NotFound,
    };

    private static AdventureSessionDto ToDto(AdventureSession s) => new(
        Id: s.Id,
        GeneratedAdventureId: s.GeneratedAdventureId,
        CampaignId: s.CampaignId,
        SessionId: s.SessionId,
        CharacterId: s.CharacterId,
        CurrentSceneId: s.CurrentSceneId,
        Status: s.Status.ToString(),
        OutcomeNotes: s.OutcomeNotes,
        StartedAt: s.StartedAt,
        CompletedAt: s.CompletedAt,
        LastActivityAt: s.LastActivityAt);
}
