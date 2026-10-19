using System.Security.Claims;
using Aircane.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Authorization;

/// <summary>Whether a participant token holds a seat at a play session's table.</summary>
public enum SeatStatus
{
    /// <summary>An approved participant of this session.</summary>
    Allowed,

    /// <summary>The token is for another session, or its participant is not on this session's roster.</summary>
    NotInSession,

    /// <summary>On the roster, but the host has not approved them yet.</summary>
    Pending,

    /// <summary>The host denied them.</summary>
    Denied,
}

/// <summary>
/// The check behind every Player-visible adventure read and action: the caller's token must have been
/// issued for this play session, to a participant who is on its roster, approved, and not denied.
/// A token's signature proves only who issued it; whether the host has since let the participant in (or
/// turned them away) lives in the roster, so it is looked up each time.
/// </summary>
public static class PlayerSeat
{
    private const string SessionIdClaim = "sid";
    private const string ParticipantIdClaim = "pid";

    public static async Task<SeatStatus> CheckAsync(
        ClaimsPrincipal user,
        Guid playSessionId,
        ISessionHostingService sessions,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst(SessionIdClaim)?.Value, out var tokenSessionId) || tokenSessionId != playSessionId)
            return SeatStatus.NotInSession;
        if (!Guid.TryParse(user.FindFirst(ParticipantIdClaim)?.Value, out var participantId))
            return SeatStatus.NotInSession;

        var participant = await sessions.GetParticipantAsync(playSessionId, participantId, cancellationToken);
        if (participant is null)
            return SeatStatus.NotInSession;
        if (participant.IsDenied)
            return SeatStatus.Denied;
        return participant.IsApproved ? SeatStatus.Allowed : SeatStatus.Pending;
    }

    /// <summary>
    /// The 403 for a seat that is not <see cref="SeatStatus.Allowed"/>. A denied participant's body carries
    /// <c>code: "participant-denied"</c>, the same as the refusal their revoked token gets, so a client has
    /// one thing to look for.
    /// </summary>
    public static ObjectResult Refusal(SeatStatus status)
        => new(ProblemFor(status)) { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>The problem body for a seat that is not <see cref="SeatStatus.Allowed"/>.</summary>
    public static ProblemDetails ProblemFor(SeatStatus status)
    {
        var problem = status switch
        {
            SeatStatus.Denied => new ProblemDetails
            {
                Title = "Join request declined",
                Detail = "The host declined your request to join this session.",
            },
            SeatStatus.Pending => new ProblemDetails
            {
                Title = "Waiting for approval",
                Detail = "The host has not approved you yet.",
            },
            _ => new ProblemDetails
            {
                Title = "Not in this session",
                Detail = "You are not a participant of this session.",
            },
        };
        problem.Status = StatusCodes.Status403Forbidden;
        if (status == SeatStatus.Denied)
            problem.Extensions["code"] = "participant-denied";

        return problem;
    }
}
