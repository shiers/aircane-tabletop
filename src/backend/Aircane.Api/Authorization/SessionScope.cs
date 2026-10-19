using System.Security.Claims;
using Aircane.Application.Abstractions;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Api.Authorization;

/// <summary>
/// The session-scope rule behind <see cref="AuthorizationPolicies.SessionMember"/>: a participant token is
/// good for its own session only (its <c>sid</c> claim), and only while the host has approved its participant
/// and not denied them. The trusted local host (<see cref="LocalHostTrust"/>) is exempt, as it is for the role
/// policies, when the app runs in a mode that trusts it.
/// </summary>
public sealed class SessionScopeRequirement : IAuthorizationRequirement
{
    public SessionScopeRequirement(bool trustLocalHost) => TrustLocalHost = trustLocalHost;

    /// <summary>Whether the trusted local host may pass without a token (desktop mode, Development).</summary>
    public bool TrustLocalHost { get; }
}

/// <summary>What a route id identifies, so the scope check can find the session it belongs to.</summary>
public enum SessionScopeKind
{
    /// <summary>A play session: compared with the token's session.</summary>
    Session,

    /// <summary>A campaign: must be the campaign of the token's session.</summary>
    Campaign,

    /// <summary>A character: must belong to the campaign of the token's session.</summary>
    Character,

    /// <summary>An adventure run: must be running in the token's session (or, with no session, its campaign).</summary>
    AdventureRun,

    /// <summary>An AI proposal: must have been made in the token's session.</summary>
    Proposal,
}

/// <summary>
/// Says what a generic route parameter (usually <c>{id}</c>) identifies. Route parameters named
/// <c>sessionId</c>, <c>campaignId</c> and <c>proposalId</c>, and a <c>campaignId</c> query parameter, are
/// recognised without it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class SessionScopedRouteAttribute : Attribute
{
    public SessionScopedRouteAttribute(SessionScopeKind kind, string routeParameter = "id")
    {
        Kind = kind;
        RouteParameter = routeParameter;
    }

    public SessionScopeKind Kind { get; }
    public string RouteParameter { get; }
}

/// <summary>
/// Lets a participant who is still waiting for approval through the scope check (the session must still
/// match). Only for reading their own status, such as reconnect.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowPendingParticipantAttribute : Attribute
{
}

/// <summary>Why the scope check failed, so the 403 can say so (see <see cref="SessionScopeResultHandler"/>).</summary>
public sealed class SessionScopeFailure : AuthorizationFailureReason
{
    public SessionScopeFailure(IAuthorizationHandler handler, SeatStatus status)
        : base(handler, $"Session scope: {status}") => Status = status;

    public SeatStatus Status { get; }
}

/// <summary>Handles <see cref="SessionScopeRequirement"/>.</summary>
public sealed class SessionScopeAuthorizationHandler : AuthorizationHandler<SessionScopeRequirement>
{
    private const string SessionIdClaim = "sid";

    private readonly ISessionHostingService _sessions;
    private readonly AircaneDbContext _db;

    public SessionScopeAuthorizationHandler(ISessionHostingService sessions, AircaneDbContext db)
    {
        _sessions = sessions;
        _db = db;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SessionScopeRequirement requirement)
    {
        // Endpoint routing passes the request; the SignalR hub does its own checks.
        if (context.Resource is not HttpContext http)
            return;

        if (requirement.TrustLocalHost && LocalHostTrust.IsTrustedLocalCaller(http))
        {
            context.Succeed(requirement);
            return;
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
            return;

        if (!Guid.TryParse(user.FindFirst(SessionIdClaim)?.Value, out var tokenSession))
        {
            context.Fail(new SessionScopeFailure(this, SeatStatus.NotInSession));
            return;
        }

        var ct = http.RequestAborted;

        // The roster check from the adventure endpoints (#52): approved and not denied. A Host token has no
        // seat to check; a participant still waiting gets through only where the endpoint allows it.
        var isHostToken = string.Equals(user.FindFirst(ClaimTypes.Role)?.Value, nameof(ParticipantRole.Host), StringComparison.OrdinalIgnoreCase);
        if (!isHostToken)
        {
            var seat = await PlayerSeat.CheckAsync(user, tokenSession, _sessions, ct);
            var allowPending = http.GetEndpoint()?.Metadata.GetMetadata<AllowPendingParticipantAttribute>() is not null;
            if (seat is SeatStatus.NotInSession or SeatStatus.Denied || (seat == SeatStatus.Pending && !allowPending))
            {
                context.Fail(new SessionScopeFailure(this, seat));
                return;
            }
        }

        foreach (var (kind, id) in TargetsOf(http))
        {
            if (!await BelongsToAsync(kind, id, tokenSession, ct))
            {
                context.Fail(new SessionScopeFailure(this, SeatStatus.NotInSession));
                return;
            }
        }

        context.Succeed(requirement);
    }

    /// <summary>Everything the request names that belongs to a session: route ids, and a campaignId query.</summary>
    internal static IEnumerable<(SessionScopeKind Kind, Guid Id)> TargetsOf(HttpContext http)
    {
        var route = http.Request.RouteValues;

        if (TryGuid(route, "sessionId", out var sessionId))
            yield return (SessionScopeKind.Session, sessionId);
        if (TryGuid(route, "campaignId", out var campaignId))
            yield return (SessionScopeKind.Campaign, campaignId);
        if (TryGuid(route, "proposalId", out var proposalId))
            yield return (SessionScopeKind.Proposal, proposalId);

        foreach (var declared in http.GetEndpoint()?.Metadata.GetOrderedMetadata<SessionScopedRouteAttribute>() ?? [])
        {
            if (TryGuid(route, declared.RouteParameter, out var id))
                yield return (declared.Kind, id);
        }

        if (Guid.TryParse(http.Request.Query["campaignId"].FirstOrDefault(), out var queryCampaign))
            yield return (SessionScopeKind.Campaign, queryCampaign);
    }

    private static bool TryGuid(RouteValueDictionary route, string key, out Guid value)
    {
        value = Guid.Empty;
        return route.TryGetValue(key, out var raw) && Guid.TryParse(raw?.ToString(), out value);
    }

    /// <summary>
    /// Whether the thing belongs to the token's session. Something that does not exist passes, so the
    /// endpoint answers 404 as it would for anyone (a 403 would only say that the id exists).
    /// </summary>
    private async Task<bool> BelongsToAsync(SessionScopeKind kind, Guid id, Guid tokenSession, CancellationToken ct)
    {
        switch (kind)
        {
            case SessionScopeKind.Session:
                return id == tokenSession;

            case SessionScopeKind.Campaign:
                return await CampaignOfAsync(tokenSession, ct) == id;

            case SessionScopeKind.Character:
            {
                var character = await _db.Characters.AsNoTracking()
                    .Where(c => c.Id == id).Select(c => new { c.CampaignId }).FirstOrDefaultAsync(ct);
                if (character is null)
                    return true;
                // A character belongs to a campaign, and so to every session of that campaign. One with no
                // campaign is the host's own and belongs to no session.
                return character.CampaignId is { } campaign && campaign == await CampaignOfAsync(tokenSession, ct);
            }

            case SessionScopeKind.AdventureRun:
            {
                var run = await _db.AdventureSessions.AsNoTracking()
                    .Where(r => r.Id == id).Select(r => new { r.SessionId, r.CampaignId }).FirstOrDefaultAsync(ct);
                if (run is null)
                    return true;
                return run.SessionId is { } runSession
                    ? runSession == tokenSession
                    : run.CampaignId == await CampaignOfAsync(tokenSession, ct);
            }

            case SessionScopeKind.Proposal:
            {
                var proposalSession = await _db.AiActionProposals.AsNoTracking()
                    .Where(p => p.Id == id).Select(p => (Guid?)p.SessionId).FirstOrDefaultAsync(ct);
                return proposalSession is null || proposalSession == tokenSession;
            }

            default:
                return false;
        }
    }

    private async Task<Guid?> CampaignOfAsync(Guid sessionId, CancellationToken ct)
        => await _db.Sessions.AsNoTracking()
            .Where(s => s.Id == sessionId).Select(s => (Guid?)s.CampaignId).FirstOrDefaultAsync(ct);
}

/// <summary>
/// Turns a failed session-scope check into the same 403 problem the adventure endpoints send
/// (<see cref="PlayerSeat.ProblemFor"/>): "Waiting for approval", "Not in this session", or a denial with
/// <c>code: "participant-denied"</c>. Every other result is handled as usual.
/// </summary>
public sealed class SessionScopeResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var failure = authorizeResult.Forbidden
            ? authorizeResult.AuthorizationFailure?.FailureReasons.OfType<SessionScopeFailure>().FirstOrDefault()
            : null;
        if (failure is null)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            PlayerSeat.ProblemFor(failure.Status),
            (System.Text.Json.JsonSerializerOptions?)null,
            "application/problem+json",
            context.RequestAborted);
    }
}
