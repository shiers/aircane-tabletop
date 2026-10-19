using System.Security.Claims;
using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// The player-safe view of the adventure that is running in a play session, for the people playing it.
/// <para>
/// The full run state (<c>GET /api/adventure-sessions/{id}</c>) is a host tool and carries more than a
/// player should see. This read is built for players and returns only what the table already knows; see
/// <see cref="PlayerAdventureRunDto"/> for what is in and out. It also needs no run id or campaign id: a
/// player knows their play session, and that is the key.
/// </para>
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId:guid}")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.SessionMember)]
public sealed class SessionAdventureRunController : ControllerBase
{
    private const string ParticipantIdClaim = "pid";

    private readonly IAdventureSessionService _adventureSessions;
    private readonly ISessionHostingService _sessions;

    public SessionAdventureRunController(
        IAdventureSessionService adventureSessions,
        ISessionHostingService sessions)
    {
        _adventureSessions = adventureSessions;
        _sessions = sessions;
    }

    /// <summary>
    /// The adventure run in progress in this play session, as a Player may see it. 404 when there is none.
    /// <list type="bullet">
    ///   <item>403 unless the caller is the host or an <b>approved</b> participant of <i>this</i> session
    ///   (a token for another session, or a participant still waiting for approval, is refused).</item>
    ///   <item>Never contains the AI's private DM note, DM notes, hidden secrets or unrevealed content.</item>
    /// </list>
    /// </summary>
    [HttpGet("adventure-run")]
    [ProducesResponseType(typeof(PlayerAdventureRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdventureRun(Guid sessionId, CancellationToken cancellationToken)
    {
        Guid? callerParticipantId = null;

        if (!this.IsHostCaller())
        {
            // A participant of THIS session whom the host has approved and not denied.
            var seat = await PlayerSeat.CheckAsync(User, sessionId, _sessions, cancellationToken);
            if (seat != SeatStatus.Allowed)
                return PlayerSeat.Refusal(seat);

            callerParticipantId = Guid.Parse(User.FindFirst(ParticipantIdClaim)!.Value);
        }

        var view = await _adventureSessions.GetPlayerRunViewAsync(sessionId, callerParticipantId, cancellationToken);
        if (view is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "No adventure in progress",
                Detail = "This session has no adventure in progress.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        // The state changes with every turn: never serve a stale copy.
        Response.Headers.CacheControl = "no-store";
        return Ok(view);
    }

}

/// <summary>Who the caller is, for controllers that check it themselves rather than through a policy.</summary>
internal static class CallerExtensions
{
    /// <summary>
    /// True for a Host token, or, with no token, the trusted local host (<see cref="LocalHostTrust"/>).
    /// </summary>
    public static bool IsHostCaller(this ControllerBase controller)
        => controller.User.Identity?.IsAuthenticated == true
            ? string.Equals(controller.User.FindFirst(ClaimTypes.Role)?.Value, nameof(ParticipantRole.Host), StringComparison.OrdinalIgnoreCase)
            : LocalHostTrust.IsTrustedLocalCaller(controller.HttpContext);
}
