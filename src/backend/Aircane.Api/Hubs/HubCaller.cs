using System.Security.Claims;
using Aircane.Api.Authorization;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Hubs;

/// <summary>
/// Who is on the other end of a hub connection, taken from the connection's token, never from a
/// message: a client can put anything in a message body.
/// </summary>
/// <param name="IsHost">A Host token, or the trusted local host (no token, see <see cref="LocalHostTrust"/>).</param>
/// <param name="SessionId">The session the token was issued for; null for the trusted local host, who may host any.</param>
/// <param name="ParticipantId">The participant the token was issued to; null for the trusted local host.</param>
/// <param name="DisplayName">The name in the token ("Host" for the trusted local host).</param>
/// <param name="Role">The role in the token.</param>
public sealed record HubCaller(bool IsHost, Guid? SessionId, Guid? ParticipantId, string DisplayName, ParticipantRole Role)
{
    private const string SessionIdClaim = "sid";
    private const string ParticipantIdClaim = "pid";
    private const string DisplayNameClaim = "dname";

    /// <summary>The caller of this connection, or null when it is neither a participant token nor the trusted local host.</summary>
    public static HubCaller? From(HubCallerContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            if (!Guid.TryParse(user.FindFirst(SessionIdClaim)?.Value, out var sessionId)
                || !Guid.TryParse(user.FindFirst(ParticipantIdClaim)?.Value, out var participantId)
                || !Enum.TryParse<ParticipantRole>(user.FindFirst(ClaimTypes.Role)?.Value, ignoreCase: true, out var role))
            {
                return null;
            }

            return new HubCaller(
                IsHost: role == ParticipantRole.Host,
                SessionId: sessionId,
                ParticipantId: participantId,
                DisplayName: user.FindFirst(DisplayNameClaim)?.Value ?? "Player",
                Role: role);
        }

        // No token: only the person at the host machine itself (a direct loopback request; tunnel and
        // proxy traffic does not count) is anyone, and they are the host.
        return LocalHostTrust.IsTrustedLocalCaller(context.GetHttpContext())
            ? new HubCaller(IsHost: true, SessionId: null, ParticipantId: null, DisplayName: "Host", Role: ParticipantRole.Host)
            : null;
    }
}
