using System.Net;

namespace Aircane.Api.Authorization;

/// <summary>
/// Decides whether a request comes from the person running Aircane on this machine, which desktop
/// mode (<c>Aircane:TrustLocalHost</c>) and Development treat as the host without a token.
/// <para>
/// A loopback TCP peer is not enough on its own. The backend can bind every interface so LAN players
/// can reach it, and a reverse proxy on this machine forwards other people's traffic from 127.0.0.1:
/// the Cloudflare tunnel (<c>cloudflared --url http://localhost:port</c>) delivers every internet
/// request that way, and so does the Vite dev server (players load the SPA from <c>:5173</c> and its
/// proxy calls the backend). A request is trusted only when it is a direct loopback request, or one
/// a local proxy says came from this machine:
/// </para>
/// <list type="number">
///   <item>The TCP peer is loopback.</item>
///   <item>Every address a proxy reports for the client (<c>X-Forwarded-For</c>, <c>Forwarded</c>,
///   <c>X-Real-IP</c>, <c>True-Client-IP</c>, <c>CF-Connecting-IP</c>) is loopback. One address that is not
///   loopback, or cannot be read as an address, withdraws trust.</item>
///   <item>The host name the client used (<c>Host</c>, and <c>X-Forwarded-Host</c> if present) is
///   <c>localhost</c> or a loopback address. A tunnel request carries the public
///   <c>*.trycloudflare.com</c> name.</item>
///   <item>It is not marked as coming from Cloudflare (<c>CF-Ray</c>).</item>
/// </list>
/// <para>
/// Cloudflare's edge always adds <c>CF-Connecting-IP</c> and appends the real client to
/// <c>X-Forwarded-For</c>, and the client cannot remove them, so tunnel traffic fails 2, 3 and 4.
/// A tunnel tool that adds no proxy header and also rewrites the <c>Host</c> header to localhost
/// cannot be told apart from the host's own browser; such tools are unsupported (docs/setup/internet-play.md).
/// </para>
/// </summary>
public static class LocalHostTrust
{
    /// <summary>Headers whose value is one or more client addresses.</summary>
    private static readonly string[] AddressHeaders =
        ["X-Forwarded-For", "X-Real-IP", "True-Client-IP", "CF-Connecting-IP"];

    /// <summary>True for a direct request from this machine (see the class remarks). Null is never trusted.</summary>
    public static bool IsTrustedLocalCaller(HttpContext? context)
    {
        if (context is null)
            return false;

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null || !IsLoopback(remoteIp))
            return false;

        var headers = context.Request.Headers;

        if (headers.ContainsKey("CF-Ray"))
            return false;

        foreach (var header in AddressHeaders)
        {
            if (headers.TryGetValue(header, out var values)
                && !values.SelectMany(v => SplitList(v)).All(IsLoopbackAddress))
            {
                return false;
            }
        }

        if (headers.TryGetValue("Forwarded", out var forwarded)
            && !forwarded.SelectMany(v => ForwardedForTokens(v)).All(IsLoopbackAddress))
        {
            return false;
        }

        if (headers.TryGetValue("X-Forwarded-Host", out var forwardedHosts)
            && !forwardedHosts.SelectMany(v => SplitList(v)).All(IsLoopbackHostName))
        {
            return false;
        }

        return IsLoopbackHostName(context.Request.Host.Host);
    }

    private static bool IsLoopback(IPAddress address)
        => IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    /// <summary>Splits a comma-separated header value, dropping empty entries.</summary>
    private static IEnumerable<string> SplitList(string? value)
        => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The <c>for=</c> values of an RFC 7239 <c>Forwarded</c> header, quotes removed.</summary>
    private static IEnumerable<string> ForwardedForTokens(string? value)
    {
        foreach (var element in SplitList(value))
        {
            foreach (var pair in element.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (pair.StartsWith("for=", StringComparison.OrdinalIgnoreCase))
                    yield return pair[4..].Trim('"');
            }
        }
    }

    /// <summary>
    /// True for a loopback IP, with an optional port or IPv6 brackets. Anything else, including the
    /// non-address placeholders proxies can send (<c>unknown</c>, <c>_hidden</c>), is not loopback.
    /// </summary>
    private static bool IsLoopbackAddress(string value)
    {
        var text = value.Trim();
        if (IPAddress.TryParse(text, out var ip))
            return IsLoopback(ip);
        return IPEndPoint.TryParse(text, out var endPoint) && IsLoopback(endPoint.Address);
    }

    private static bool IsLoopbackHostName(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        // Drop an optional port: "localhost:5173", "127.0.0.1:5173", "[::1]:5173". A bare IPv6
        // address ("::1") has several colons and no port.
        var text = host.Trim();
        string name;
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end < 0)
                return false;
            name = text[1..end];
        }
        else
        {
            name = text.Count(c => c == ':') == 1 ? text[..text.IndexOf(':')] : text;
        }

        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(name, out var ip) && IsLoopback(ip);
    }
}
