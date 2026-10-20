using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Api.Authorization;

/// <summary>
/// The JWT bearer events for participant tokens: SignalR token passing, per-request revocation, and the
/// clear refusal for a participant the host denied. Shared by <c>Program</c> and the tests that register
/// the bearer scheme themselves, so they exercise the handlers the app really runs.
/// </summary>
public static class ParticipantJwtEvents
{
    /// <summary>HttpContext.Items key: the bearer token on this request belongs to a participant the host denied.</summary>
    private const string ParticipantDeniedItem = "Aircane.ParticipantDenied";

    /// <summary>HttpContext.Items key: the bearer token on this request belongs to a participant the host removed.</summary>
    private const string ParticipantRemovedItem = "Aircane.ParticipantRemoved";

    public static JwtBearerEvents Create() => new()
    {
        // Allow SignalR connections to pass the token as a query parameter (?access_token=...)
        // because browsers cannot set Authorization headers on WebSocket connections.
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },

        // Enforce token revocation on every [Authorize] request and hub connection, not
        // just the manual reconnect path. Checks the in-memory session fast path and the
        // persistent per-token store so a revoked token is rejected even after a restart.
        OnTokenValidated = async context =>
        {
            var revocation = context.HttpContext.RequestServices
                .GetRequiredService<ITokenRevocationService>();

            var sessionIdClaim = context.Principal?.FindFirst("sid")?.Value;
            if (Guid.TryParse(sessionIdClaim, out var sessionId)
                && revocation.IsSessionRevoked(sessionId))
            {
                context.Fail("Session has been revoked.");
                return;
            }

            var jti = context.Principal?.FindFirst(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrEmpty(jti)
                && await revocation.IsTokenRevokedAsync(jti, context.HttpContext.RequestAborted))
            {
                // A host who denied or removed this person revoked the token on purpose. Remember which, so
                // the challenge below can say so instead of answering with a bare 401 that looks like an
                // expired login.
                var participantClaim = context.Principal?.FindFirst("pid")?.Value;
                if (Guid.TryParse(participantClaim, out var participantId))
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<AircaneDbContext>();
                    var seat = await db.SessionParticipants.AsNoTracking()
                        .Where(p => p.Id == participantId)
                        .Select(p => new { p.IsDenied, Removed = p.RemovedAt != null })
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                    if (seat?.IsDenied == true)
                        context.HttpContext.Items[ParticipantDeniedItem] = true;
                    else if (seat?.Removed == true)
                        context.HttpContext.Items[ParticipantRemovedItem] = true;
                }

                context.Fail("Token has been revoked.");
            }
        },

        // A denied or removed participant gets a clear refusal (403, with the reason), not a generic 401.
        OnChallenge = async context =>
        {
            if (context.HttpContext.Items.ContainsKey(ParticipantRemovedItem))
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = PlayerSeat.RemovedTitle,
                    Detail = PlayerSeat.RemovedDetail,
                    Status = StatusCodes.Status403Forbidden,
                    Extensions = { ["code"] = PlayerSeat.RemovedCode },
                }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", context.HttpContext.RequestAborted);
                return;
            }

            if (!context.HttpContext.Items.ContainsKey(ParticipantDeniedItem))
                return;

            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Join request declined",
                Detail = "The host declined your request to join this session. You can join again with the invite code if you think this was a mistake.",
                Status = StatusCodes.Status403Forbidden,
                Extensions = { ["code"] = "participant-denied" },
            }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", context.HttpContext.RequestAborted);
        },
    };
}
