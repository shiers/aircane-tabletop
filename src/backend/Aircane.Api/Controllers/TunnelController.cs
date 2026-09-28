using Aircane.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Receives the Cloudflare Tunnel public URL from the desktop wrapper and toggles the
/// backend's internet-mode state.
/// <para>
/// The tunnel is started by the desktop wrapper (`cloudflared`), which then reports the
/// issued public URL here. Recording it puts the backend into internet mode, switching on
/// the rate-limiting and CSRF hardening and surfacing the URL in
/// <c>GET /api/sessions/network-info</c>. The state is in-memory only — a restart drops it,
/// matching the free tunnel's per-session URL.
/// </para>
/// <para>
/// <b>Security:</b> because this endpoint controls a process-wide security switch, it only
/// accepts requests from the loopback interface (the local desktop wrapper). Remote clients
/// reaching the backend through the tunnel cannot toggle internet mode.
/// </para>
/// </summary>
[ApiController]
[Produces("application/json")]
[AllowAnonymous]
public sealed class TunnelController : ControllerBase
{
    private readonly ITunnelStateService _tunnelState;
    private readonly ILogger<TunnelController> _logger;

    public TunnelController(
        ITunnelStateService tunnelState,
        ILogger<TunnelController> logger)
    {
        _tunnelState = tunnelState;
        _logger = logger;
    }

    /// <summary>
    /// Records the active tunnel URL and enters internet mode. Loopback callers only.
    /// </summary>
    [HttpPost("api/sessions/tunnel-url")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult SetTunnelUrl([FromBody] SetTunnelUrlBody body)
    {
        if (!IsLoopbackCaller())
            return LoopbackOnly();

        if (string.IsNullOrWhiteSpace(body?.TunnelUrl))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "tunnelUrl is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            _tunnelState.SetTunnelActive(body.TunnelUrl.Trim());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid tunnel URL",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }

        return NoContent();
    }

    /// <summary>
    /// Clears the tunnel URL and leaves internet mode. Loopback callers only.
    /// </summary>
    [HttpDelete("api/sessions/tunnel-url")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult ClearTunnelUrl()
    {
        if (!IsLoopbackCaller())
            return LoopbackOnly();

        _tunnelState.ClearTunnel();
        return NoContent();
    }

    /// <summary>
    /// True when the request originated from the loopback interface (the local desktop
    /// wrapper). Requests forwarded through the tunnel carry a non-loopback remote address.
    /// </summary>
    private bool IsLoopbackCaller()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
            return false;
        return System.Net.IPAddress.IsLoopback(remoteIp);
    }

    private IActionResult LoopbackOnly()
    {
        _logger.LogWarning(
            "Rejected non-loopback attempt to change tunnel state. RemoteIp={RemoteIp}",
            HttpContext.Connection.RemoteIpAddress);
        return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Title = "Forbidden",
            Detail = "Tunnel state can only be changed from the local host.",
            Status = StatusCodes.Status403Forbidden,
        });
    }
}

/// <summary>Request body for reporting the active tunnel URL.</summary>
public sealed record SetTunnelUrlBody(string TunnelUrl);
