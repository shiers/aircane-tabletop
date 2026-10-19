using System.Net;

namespace Aircane.Api.Authorization;

/// <summary>
/// Decides whether a request comes from the person running Aircane on this machine, which desktop
/// mode (<c>Aircane:TrustLocalHost</c>) treats as the host without a token.
/// <para>
/// A loopback TCP peer is not enough on its own. The desktop backend binds every interface so LAN
/// players can reach it, and the Cloudflare tunnel runs <c>cloudflared --url http://localhost:port</c>
/// on the host machine, so every internet request arrives from 127.0.0.1. A request is trusted only
/// when it is a direct loopback request: a loopback peer, no header a proxy adds to say it is
/// forwarding for someone else, and a loopback host name (a tunnel request carries the public
/// <c>*.trycloudflare.com</c> host). Either signal alone excludes the tunnel; both are required.
/// </para>
/// </summary>
public static class LocalHostTrust
{
    /// <summary>
    /// Headers a reverse proxy adds for the client it is forwarding. Cloudflare's edge always adds
    /// <c>CF-Connecting-IP</c> and <c>X-Forwarded-For</c>, and a client cannot remove them.
    /// </summary>
    private static readonly string[] ProxyHeaders =
    [
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Real-IP",
        "True-Client-IP",
        "CF-Connecting-IP",
        "CF-Ray",
    ];

    /// <summary>True for a direct request from this machine (see the class remarks). Null is never trusted.</summary>
    public static bool IsTrustedLocalCaller(HttpContext? context)
    {
        if (context is null)
            return false;

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
            return false;
        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        if (!IPAddress.IsLoopback(remoteIp))
            return false;

        foreach (var header in ProxyHeaders)
        {
            if (context.Request.Headers.ContainsKey(header))
                return false;
        }

        return IsLoopbackHostName(context.Request.Host.Host);
    }

    private static bool IsLoopbackHostName(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);
    }
}

/// <summary>
/// Whether a request that carries no participant token counts as the host, for actions that check
/// the caller's role themselves rather than through a policy (the adventure endpoints). Registered by
/// <see cref="AuthorizationServiceExtensions.AddParticipantAuthorization"/> to match the policies:
/// in Development anyone (every policy is open there), otherwise only the trusted local host.
/// </summary>
public sealed class AnonymousHostRule
{
    /// <summary>Only a direct loopback request (<see cref="LocalHostTrust"/>). The default when none is registered.</summary>
    public static readonly AnonymousHostRule LocalHostOnly = new(trustAnyone: false);

    private readonly bool _trustAnyone;

    public AnonymousHostRule(bool trustAnyone)
    {
        _trustAnyone = trustAnyone;
    }

    /// <summary>True when a token-less <paramref name="context"/> is the host under this rule.</summary>
    public bool IsHost(HttpContext context) => _trustAnyone || LocalHostTrust.IsTrustedLocalCaller(context);
}
