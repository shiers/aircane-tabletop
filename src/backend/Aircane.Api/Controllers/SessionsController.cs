using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.Configuration;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aircane.Api.Controllers;

/// <summary>
/// Session management - create sessions under a campaign and retrieve session state.
/// <para>
/// Role checks are declared per action, not on the class: ASP.NET Core requires every
/// <c>[Authorize]</c> on an endpoint, so a class-level HostOnly would also apply to the player
/// actions marked <see cref="AuthorizationPolicies.Authenticated"/> (reconnect, session and
/// participant reads) and lock players out of them.
/// </para>
/// <para>
/// The session scope is on the class (<see cref="AuthorizationPolicies.SessionMember"/>), which every
/// player action needs too: a token is good for its own session only, held by an approved participant.
/// Reconnect also takes a participant still waiting (it is how they learn their status); join and
/// join-info are anonymous.
/// </para>
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.SessionMember)]
[SessionScopedRoute(SessionScopeKind.Session)]
public sealed class SessionsController : ControllerBase
{
    private readonly ISessionHostingService _sessions;
    private readonly IParticipantTokenService _tokenService;
    private readonly ITunnelStateService _tunnelState;
    private readonly IValidator<CreateSessionRequest> _createValidator;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionHostingService sessions,
        IParticipantTokenService tokenService,
        ITunnelStateService tunnelState,
        IValidator<CreateSessionRequest> createValidator,
        ILogger<SessionsController> logger)
    {
        _sessions = sessions;
        _tokenService = tokenService;
        _tunnelState = tunnelState;
        _createValidator = createValidator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new session under the specified campaign.
    /// Returns the session details including the plain-text invite code (returned once only).
    /// </summary>
    [HttpPost("api/campaigns/{campaignId:guid}/start-session")]
    [Authorize(Policy = AuthorizationPolicies.HostOnly)]
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
    [EnableRateLimiting(RateLimitingSettings.JoinPolicy)]
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
    /// What the join page needs before the player has a token: the session's name and whether it can
    /// be joined. Anonymous, rate-limited like join, and minimal on purpose: no participants, invite
    /// code, campaign or access mode. A session that does not exist, has ended, or is a solo play
    /// session answers exactly like any other that cannot be joined (HTTP 200, no name), so the
    /// response does not confirm that an ID was ever valid. Only a running session open to players
    /// reveals anything, and the invite code, which is what joining actually needs, is never
    /// returned. Session IDs are random GUIDs, so they cannot be enumerated.
    /// </summary>
    [HttpGet("api/sessions/{id:guid}/join-info")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSettings.JoinPolicy)]
    [ProducesResponseType(typeof(JoinInfoDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<JoinInfoDto>> GetJoinInfo(Guid id, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetSessionAsync(id, cancellationToken);

        // The same condition JoinSessionAsync enforces (an active session), plus: a solo play session
        // exists only to carry a solo run's live events, and its name is the adventure's title.
        var joinable = session is { Status: SessionStatus.Active, AccessMode: not SessionAccessMode.Solo };

        // Never cache: a session stops being joinable the moment the host ends it.
        Response.Headers.CacheControl = "no-store";
        return Ok(joinable ? new JoinInfoDto(session!.Name, Joinable: true) : JoinInfoDto.NotJoinable);
    }

    /// <summary>
    /// Reconnects a participant using a valid existing token.
    /// Restores the participant's session identity and role without requiring re-approval.
    /// The token must be passed as a Bearer header: Authorization: Bearer {token}
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/reconnect")]
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [AllowPendingParticipant]
    [EnableRateLimiting(RateLimitingSettings.JoinPolicy)]
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
        var claims = await _tokenService.ValidateTokenAsync(token, cancellationToken);

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

        // Reconnecting never needs approval: a participant who is still waiting reconnects fine, but is not
        // yet allowed to act. Say which, so a waiting page that was reloaded knows where it stands.
        var roster = await _sessions.GetParticipantsAsync(id, cancellationToken);
        var isApproved = roster.Any(p => p.Id == claims.ParticipantId && p.IsApproved);

        return Ok(new ReconnectResult(
            SessionId: claims.SessionId,
            ParticipantId: claims.ParticipantId,
            DisplayName: claims.DisplayName,
            Role: claims.Role,
            IsApproved: isApproved));
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
    [Authorize(Policy = AuthorizationPolicies.HostOnly)]
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
    /// Returns all participants in the session. The host and a human DM get every detail; a Player gets
    /// the approved participants as they are, and of those still waiting only their name (no join time,
    /// character or other detail). Denied people are on nobody's list.
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
        if (this.IsHostOrDm())
            return Ok(participants);

        return Ok(participants
            .Select(p => p.IsApproved
                ? p
                : new ParticipantDto(p.Id, p.SessionId, p.DisplayName, p.Role, CharacterId: null, IsApproved: false,
                    JoinedAt: default, LastSeenAt: default))
            .ToList());
    }

    /// <summary>
    /// Approves a pending participant, granting them access to the session.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/approve-participant")]
    [Authorize(Policy = AuthorizationPolicies.HostOnly)]
    [EnableRateLimiting(RateLimitingSettings.JoinPolicy)]
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cannot approve",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    /// <summary>
    /// Denies a pending join request (Host only). The participant is marked denied and dropped from the
    /// roster, their token is revoked so every later request with it is refused (403, "join request
    /// declined"), and the session's group is told (<c>ParticipantDenied</c>). Only a pending request can
    /// be denied (409 for someone already approved). Denying twice is harmless. A denied person may join
    /// again with the invite code; that is a new request.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/deny-participant")]
    [Authorize(Policy = AuthorizationPolicies.HostOnly)]
    [EnableRateLimiting(RateLimitingSettings.JoinPolicy)]
    [ProducesResponseType(typeof(ParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DenyParticipant(
        Guid id,
        [FromBody] DenyParticipantBody body,
        CancellationToken cancellationToken)
    {
        if (body is null || body.ParticipantId == Guid.Empty)
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
            var dto = await _sessions.DenyParticipantAsync(id, body.ParticipantId, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cannot deny",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
    }

    /// <summary>
    /// Assigns a character to a participant in the session.
    /// </summary>
    [HttpPost("api/sessions/{id:guid}/assign-character")]
    [Authorize(Policy = AuthorizationPolicies.HostOnly)]
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
    [Authorize(Policy = AuthorizationPolicies.HostOnly)]
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

    /// <summary>
    /// Returns network information for reaching this host: the loopback URL and,
    /// when available, the LAN URL other devices on the same network can use.
    /// Consumed by the desktop wrapper (window title, tray menu, QR code) and by
    /// the in-app session screen.
    /// </summary>
    /// <remarks>
    /// Anonymous: this endpoint exposes only the host's own reachable URLs (no
    /// session data, no secrets), and the desktop wrapper calls it during startup
    /// before any participant token exists. The plaintext invite code is never
    /// returned here — it is available only once, at session creation.
    /// </remarks>
    [HttpGet("api/sessions/network-info")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(NetworkInfoDto), StatusCodes.Status200OK)]
    public ActionResult<NetworkInfoDto> GetNetworkInfo()
    {
        // Derive the port from the current request so the URLs match however the
        // host launched the backend (ASPNETCORE_URLS, launchSettings, etc.).
        var port = Request.Host.Port ?? 5000;
        var localUrl = $"http://localhost:{port}";

        var lanIp = TryGetLanIpv4();
        var lanUrl = lanIp is null ? null : $"http://{lanIp}:{port}";

        var tunnelUrl = _tunnelState.TunnelUrl;

        return Ok(new NetworkInfoDto(
            localUrl,
            lanUrl,
            InviteCode: null,
            TunnelUrl: tunnelUrl,
            TunnelActive: _tunnelState.IsInternetModeActive));
    }

    /// <summary>
    /// Best-effort discovery of the machine's primary LAN IPv4 address.
    /// Returns null if none can be determined (offline, loopback-only, etc.).
    /// </summary>
    private static string? TryGetLanIpv4()
    {
        try
        {
            // Opening a UDP socket to a routable address lets the OS pick the
            // outbound interface without actually sending any packets.
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is System.Net.IPEndPoint endpoint
                && !System.Net.IPAddress.IsLoopback(endpoint.Address))
            {
                return endpoint.Address.ToString();
            }
        }
        catch
        {
            // Fall through to the interface scan below.
        }

        try
        {
            // Fallback: scan active non-loopback interfaces for a private IPv4.
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // No LAN address determinable.
        }

        return null;
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

/// <summary>Request body for denying a pending join request. The session ID comes from the route.</summary>
public sealed record DenyParticipantBody(
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
    string Role,
    bool IsApproved = false);
