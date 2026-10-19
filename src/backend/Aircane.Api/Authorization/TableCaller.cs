using System.Security.Claims;
using Aircane.Api.Controllers;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Authorization;

/// <summary>A participant token's identity: the session it was issued for, its participant and role.</summary>
public sealed record TableCaller(Guid SessionId, Guid ParticipantId, ParticipantRole Role);

/// <summary>
/// Who is calling, for the reads that answer differently by role. The session check itself is the
/// <see cref="AuthorizationPolicies.SessionMember"/> policy; this only says what the caller may see once in.
/// </summary>
public static class TableCallerExtensions
{
    /// <summary>The token's identity, or null for the trusted local host (no token) or a token without one.</summary>
    public static TableCaller? TokenCaller(this ControllerBase controller)
    {
        var user = controller.User;
        if (user.Identity?.IsAuthenticated != true)
            return null;
        return Guid.TryParse(user.FindFirst("sid")?.Value, out var sessionId)
               && Guid.TryParse(user.FindFirst("pid")?.Value, out var participantId)
               && Enum.TryParse<ParticipantRole>(user.FindFirst(ClaimTypes.Role)?.Value, ignoreCase: true, out var role)
            ? new TableCaller(sessionId, participantId, role)
            : null;
    }

    /// <summary>
    /// The host (Host token or the trusted local host) or a human DM: sees DM-facing content of their own
    /// session (private rolls, NPC sheets, full combat state).
    /// </summary>
    public static bool IsHostOrDm(this ControllerBase controller)
        => controller.IsHostCaller() || controller.TokenCaller()?.Role == ParticipantRole.HumanDm;
}
