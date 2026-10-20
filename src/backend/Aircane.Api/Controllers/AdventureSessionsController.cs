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
/// Adventure execution: start a run of a generated adventure, read its state, play it
/// through the AI DM, and override it manually. The caller must be the Host, or a Player
/// who holds an approved seat at the run's play session. A group run (several players taking turns)
/// additionally gates player actions on whose turn it is; the Host passes the turn and adds players,
/// and players read the run through the player-safe <c>GET /api/sessions/{id}/adventure-run</c>.
/// </summary>
[ApiController]
[Route("api/adventure-sessions")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.SessionMember)]
[SessionScopedRoute(SessionScopeKind.AdventureRun)]
public sealed class AdventureSessionsController : ControllerBase
{
    // Claim carrying the session a participant token was issued for (see ParticipantTokenService).
    private const string SessionIdClaim = "sid";

    // Claim carrying the participant a token was issued to; who is acting in a group run.
    private const string ParticipantIdClaim = "pid";

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
    /// Starts a group run of an approved adventure: the listed participants of a play session take turns,
    /// in the order given. The play session is <c>playSessionId</c> when the request names one (it must belong
    /// to the campaign and not have ended: 404 if it does not exist, 400 otherwise), and otherwise the
    /// campaign's most recent play session that has not ended. 201 when a run is created; 200 with the
    /// existing run when the campaign is already running this adventure as a group (idempotent); 409 when
    /// that existing run belongs to a different play session than the one named.
    /// </summary>
    [HttpPost("start-group")]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartGroupSession(
        [FromBody] StartGroupAdventureSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsHost())
            return Forbid();

        if (request is null || request.GeneratedAdventureId == Guid.Empty || request.CampaignId == Guid.Empty)
            return Problem400("generatedAdventureId and campaignId are required.");
        if (request.ParticipantIds is null || request.ParticipantIds.Count == 0)
            return Problem400("participantIds must list at least one participant.");
        if (request.PlaySessionId == Guid.Empty)
            return Problem400("playSessionId must be a play session id, or left out.");

        try
        {
            var active = await _adventureSessions.GetActiveByCampaignAsync(request.CampaignId, cancellationToken);
            var resuming = active is not null
                           && active.IsGroup
                           && active.GeneratedAdventureId == request.GeneratedAdventureId;
            if (active is not null && !resuming)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Adventure already in progress",
                    Detail = active.GeneratedAdventureId == request.GeneratedAdventureId
                        ? "This campaign is already running this adventure as a solo run. Abandon or complete it before starting a group run."
                        : "This campaign already has an adventure in progress. Abandon or complete it before starting another.",
                    Status = StatusCodes.Status409Conflict,
                });
            }

            // Resuming returns the run as it is, and that run belongs to one play session. If the host named a
            // different one, the players waiting there are not in it: say so instead of handing back a run
            // they cannot join.
            if (resuming && request.PlaySessionId is { } named && active!.SessionId != named)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Adventure already running in another session",
                    Detail = "This campaign is already running this adventure in a different play session, so the players in the session you named are not in it. End or finish that run first.",
                    Status = StatusCodes.Status409Conflict,
                });
            }

            var session = await _adventureSessions.StartGroupSessionAsync(
                request.GeneratedAdventureId, request.CampaignId, request.ParticipantIds, request.PlaySessionId,
                request.TurnAdvance ?? TurnAdvanceMode.ManualOnly, cancellationToken);
            var state = await _adventureSessions.GetSessionStateAsync(session.Id, cancellationToken);

            return resuming
                ? Ok(state)
                : CreatedAtAction(nameof(GetSession), new { id = session.Id }, state);
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
        var refused = await CheckAccessAsync(session, AccessLevel.Read, cancellationToken);
        if (refused is not null)
            return refused;

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
        var refused = await CheckAccessAsync(session, AccessLevel.Manage, cancellationToken);
        if (refused is not null)
            return refused;

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
        var refused = await CheckAccessAsync(session, AccessLevel.Read, cancellationToken);
        if (refused is not null)
            return refused;

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
        var refused = await CheckAccessAsync(session, AccessLevel.Read, cancellationToken);
        if (refused is not null)
            return refused;

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
        var refused = await CheckAccessAsync(session, AccessLevel.Read, cancellationToken);
        if (refused is not null)
            return refused;

        return Ok(ToDto(session));
    }

    /// <summary>
    /// Submits a free-text player action to the AI DM for this run. Returns the narration, the
    /// outcome of any adventure actions the AI proposed, and the refreshed run state. In a group
    /// run only the participant whose turn it is may act (403 otherwise).
    /// </summary>
    [HttpPost("{id:guid}/player-action")]
    [ProducesResponseType(typeof(AdventureSessionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
        var refused = await CheckAccessAsync(session, AccessLevel.Play, cancellationToken);
        if (refused is not null)
            return refused;
        if (session.Status != AdventureSessionStatus.InProgress)
            return Problem400($"Adventure session is {session.Status}; it can no longer be played.");

        return await RunAsync(async () =>
        {
            // Nobody holds the turn: everyone has left the order, or (a legacy run) the acting participant is no
            // longer in it. Say that, rather than "not your turn", which would leave every player waiting.
            if (session.IsGroup && NoActingPlayer(session) is { } noOne)
                return noOne;

            // Solo runs have no turn order, so this only applies to group runs.
            if (session.IsGroup && !IsActiveParticipant(session))
            {
                var waitingFor = (await _adventureSessions.GetSessionStateAsync(id, cancellationToken))
                    .ActiveParticipantName ?? "another player";
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Title = "Not your turn",
                    Detail = $"It is not your turn. Waiting for {waitingFor}.",
                    Status = StatusCodes.Status403Forbidden,
                });
            }

            var response = await _playerActionService.ProcessActionAsync(
                new PlayerActionRequest(
                    SessionId: session.SessionId ?? Guid.Empty,
                    CampaignId: session.CampaignId,
                    CharacterId: session.CharacterId ?? Guid.Empty,
                    ActionText: request.ActionText.Trim()),
                cancellationToken);

            // The AI answered (a failed call threw above, and left the turn where it was). A run set to
            // AfterEachAction now moves on to the next player. Best effort: the narration is already saved and is
            // what the player is waiting for, so a problem here is logged, never turned into a failed request.
            if (session.IsGroup && session.ActiveParticipantId is { } actor)
            {
                try
                {
                    await _adventureSessions.AutoAdvanceAfterActionAsync(id, actor, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Auto-advance after an action failed for adventure session {AdventureSessionId}", id);
                }
            }

            // A Player is told the narration and nothing else. The private DM note is DM-only, and the run
            // state and the AI's proposed actions carry more than the table's view (GET
            // /api/sessions/{id}/adventure-run) does: scene objectives, exits, NPCs, rolled-back errors.
            if (!IsHost())
                return Ok(new AdventureSessionActionResponse(response.Narration, null, [], null));

            var state = await _adventureSessions.GetSessionStateAsync(id, cancellationToken);
            return Ok(new AdventureSessionActionResponse(
                response.Narration,
                response.PrivateDmNote,
                response.ProposedActions.Select(AdventureActionOutcomeDto.From).ToList(),
                state));
        });
    }

    /// <summary>
    /// Passes the turn of a group run to the next participant, starting a new round when it wraps back to the
    /// first. Clears the pending roll (it was owed by the player whose turn just ended), broadcasts the adventure
    /// state change, and records who passed it.
    /// <list type="bullet">
    ///   <item><b>The host</b> may pass any turn. With <c>fromParticipantId</c> set, the pass happens only if that
    ///   participant still holds the turn; otherwise 409 and nothing changes (the host's screen was out of date).
    ///   Returns the full run state.</item>
    ///   <item><b>The acting player</b> (approved, not denied, token for this session, their <c>pid</c> holds the
    ///   turn) may end their own turn, and only while it is theirs. Returns the player-safe run.</item>
    ///   <item><b>Anyone else</b>: 403 ("Not your turn" for a player who does not hold it; a waiting or denied
    ///   participant gets their own refusal).</item>
    /// </list>
    /// A request that arrives after the turn has already moved on never moves it again: the check and the move
    /// are made together under a lock on the run. A player's late request is a 403 "not your turn" naming who
    /// has it now; a host's, with <c>fromParticipantId</c>, a 409.
    /// </summary>
    [HttpPost("{id:guid}/advance-turn")]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PlayerAdventureRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdvanceTurn(
        Guid id,
        [FromQuery] Guid? fromParticipantId,
        CancellationToken cancellationToken)
    {
        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        var refused = await CheckAccessAsync(session, AccessLevel.EndTurn, cancellationToken);
        if (refused is not null)
            return refused;

        var host = IsHost();
        Guid? callerParticipantId = null;
        if (!host)
        {
            // A player ends their own turn, and only while it is theirs.
            if (!Guid.TryParse(User.FindFirst(ParticipantIdClaim)?.Value, out var participantId)
                || !session.IsGroup || session.ActiveParticipantId != participantId)
            {
                return await NotYourTurnAsync(id, cancellationToken);
            }
            callerParticipantId = participantId;
        }

        var pass = host
            ? new TurnPass(TurnPassedBy.Host, ExpectedActiveParticipantId: fromParticipantId)
            : new TurnPass(TurnPassedBy.Player, callerParticipantId, ExpectedActiveParticipantId: callerParticipantId);

        try
        {
            await _adventureSessions.PassTurnAsync(id, pass, cancellationToken);
        }
        catch (TurnAlreadyMovedException ex)
        {
            // Moved between the check above and the move itself (or the host's screen was stale): nothing changed.
            return host
                ? Conflict(new ProblemDetails
                {
                    Title = "The turn has already moved on",
                    Detail = ex.Message,
                    Status = StatusCodes.Status409Conflict,
                })
                : StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Title = "Not your turn",
                    Detail = $"It is not your turn. Waiting for {ex.NowActiveName ?? "another player"}.",
                    Status = StatusCodes.Status403Forbidden,
                });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(NotFoundProblem(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Problem400(ex.Message);
        }

        // The host gets the whole run; a player only what the table may see (adventure-run).
        if (host)
            return Ok(await _adventureSessions.GetSessionStateAsync(id, cancellationToken));

        var view = session.SessionId is { } playSessionId
            ? await _adventureSessions.GetPlayerRunViewAsync(playSessionId, callerParticipantId, cancellationToken)
            : null;
        return view is null ? NoContent() : Ok(view);
    }

    private async Task<IActionResult> NotYourTurnAsync(Guid runId, CancellationToken cancellationToken)
    {
        string? waitingFor = null;
        try
        {
            waitingFor = (await _adventureSessions.GetSessionStateAsync(runId, cancellationToken)).ActiveParticipantName;
        }
        catch (InvalidOperationException)
        {
            // The run cannot be described (no adventure): fall through to the generic wording.
        }

        return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Title = "Not your turn",
            Detail = $"It is not your turn. Waiting for {waitingFor ?? "another player"}.",
            Status = StatusCodes.Status403Forbidden,
        });
    }

    /// <summary>
    /// Adds a participant of the run's play session to the end of a group run's turn order (Host only).
    /// </summary>
    [HttpPost("{id:guid}/participants")]
    [ProducesResponseType(typeof(AdventureSessionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddParticipant(
        Guid id,
        [FromBody] AddAdventureParticipantRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsHost())
            return Forbid();

        if (request is null || request.ParticipantId == Guid.Empty)
            return Problem400("participantId is required.");

        var session = await _adventureSessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound();
        if (session.TurnOrder.Contains(request.ParticipantId))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Already in the adventure",
                Detail = "That participant is already in this adventure run.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        return await RunAsync(async () =>
        {
            await _adventureSessions.AddParticipantAsync(id, request.ParticipantId, cancellationToken);
            return Ok(await _adventureSessions.GetSessionStateAsync(id, cancellationToken));
        });
    }

    /// <summary>
    /// Manually advances to a scene reachable from the current one (host override). In a group run
    /// only the Host may do this.
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
        var refused = await CheckAccessAsync(session, AccessLevel.Manage, cancellationToken);
        if (refused is not null)
            return refused;

        return await RunAsync(async () =>
        {
            await _adventureSessions.AdvanceSceneAsync(
                id, request.TargetSceneId, request.TransitionNarration, cancellationToken);
            return Ok(await _adventureSessions.GetSessionStateAsync(id, cancellationToken));
        });
    }

    /// <summary>
    /// Abandons the run. Idempotent for an already-abandoned run. In a group run only the Host may do this.
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
        var refused = await CheckAccessAsync(session, AccessLevel.Manage, cancellationToken);
        if (refused is not null)
            return refused;

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
        var refused = await CheckAccessAsync(session, AccessLevel.Read, cancellationToken);
        if (refused is not null)
            return refused;

        return await RunAsync(async () => Ok(await _adventureSessions.GetAvailableScenesAsync(id, cancellationToken)));
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// True when the caller is the Host: a Host token, or, with no token, the trusted local host
    /// (<see cref="LocalHostTrust"/>): a direct request from the host's own machine, never a LAN
    /// player or tunnel traffic. Desktop mode and Development both let such a caller past the
    /// policies; a hosted deployment never lets a token-less caller this far.
    /// </summary>
    private bool IsHost()
        => User.Identity?.IsAuthenticated == true
            ? string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, nameof(ParticipantRole.Host), StringComparison.OrdinalIgnoreCase)
            : LocalHostTrust.IsTrustedLocalCaller(HttpContext);

    /// <summary>What the caller wants to do with a run; each level has its own rule in <see cref="CheckAccessAsync"/>.</summary>
    private enum AccessLevel
    {
        /// <summary>Read the full run: its state, history, recap, exits, the campaign's active run.</summary>
        Read,

        /// <summary>Act in the run (player-action).</summary>
        Play,

        /// <summary>End one's own turn (advance-turn); the acting player is checked by the endpoint.</summary>
        EndTurn,

        /// <summary>Change the run's course (resume, move the scene, abandon).</summary>
        Manage,
    }

    /// <summary>
    /// Null when the caller may do this; otherwise the refusal to return (always a 403).
    /// <list type="bullet">
    ///   <item><b>Host</b> (a Host token, or the trusted local host): everything.</item>
    ///   <item><b>Player</b>: only with a seat at the run's play session, meaning a token issued for it to a
    ///   participant who is on its roster, approved and not denied (<see cref="PlayerSeat"/>). A participant
    ///   still waiting gets 403 "Waiting for approval"; a denied one 403 with <c>code: "participant-denied"</c>.</item>
    ///   <item><b>Group run</b>: a Player may only <see cref="AccessLevel.Play"/> it and end their own turn (<see cref="AccessLevel.EndTurn"/>). The full run (state,
    ///   history, recap, exits) carries scene synopses, objectives, NPCs and what comes next, so at a table
    ///   a Player reads the player-safe <c>GET /api/sessions/{id}/adventure-run</c> instead, and the course
    ///   of the game is the Host's to change. A solo run has no table to protect: its one player may do all of it.</item>
    /// </list>
    /// </summary>
    private async Task<IActionResult?> CheckAccessAsync(AdventureSession session, AccessLevel level, CancellationToken cancellationToken)
    {
        if (IsHost())
            return null;

        var isPlayer = string.Equals(
            User.FindFirst(ClaimTypes.Role)?.Value, nameof(ParticipantRole.Player), StringComparison.OrdinalIgnoreCase);
        if (!isPlayer || session.SessionId is not { } playSessionId)
            return Forbid();

        // Seat first: a participant who is waiting or was denied hears that, whatever they asked for.
        var seat = await PlayerSeat.CheckAsync(User, playSessionId, _sessionHosting, cancellationToken);
        if (seat == SeatStatus.NotInSession)
            return Forbid();
        if (seat != SeatStatus.Allowed)
            return PlayerSeat.Refusal(seat);

        return session.IsGroup && level is not (AccessLevel.Play or AccessLevel.EndTurn) ? Forbid() : null;
    }

    /// <summary>
    /// The 409 for a group run in which no one can act (<c>code: "no-acting-player"</c>), or null when someone holds
    /// the turn.
    /// </summary>
    private ObjectResult? NoActingPlayer(AdventureSession session)
    {
        if (session.ActiveParticipantId is { } active && session.TurnOrder.Contains(active))
            return null;

        var problem = new ProblemDetails
        {
            Title = "No acting player",
            Detail = session.TurnOrder.Count == 0
                ? "No one is in this adventure's turn order. The host needs to approve and add a player to continue."
                : "No one holds the turn right now. The host needs to pass the turn to continue.",
            Status = StatusCodes.Status409Conflict,
        };
        problem.Extensions["code"] = "no-acting-player";
        return Conflict(problem);
    }

    /// <summary>
    /// True when the caller's token was issued to the participant whose turn it is. A caller with no
    /// participant identity (the trusted local host) is never the active participant.
    /// </summary>
    private bool IsActiveParticipant(AdventureSession session)
        => Guid.TryParse(User.FindFirst(ParticipantIdClaim)?.Value, out var participantId)
           && session.ActiveParticipantId == participantId;

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
