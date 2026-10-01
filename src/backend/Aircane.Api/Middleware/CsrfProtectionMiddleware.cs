using Aircane.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Aircane.Api.Middleware;

/// <summary>
/// SPA-oriented CSRF protection for internet-exposed sessions.
/// <para>
/// Aircane's frontend is a Vue SPA that authenticates with JWT <b>bearer tokens</b>, not
/// cookies, so the classic ASP.NET Core antiforgery cookie/token pair does not apply — a
/// cross-site page cannot read another origin's bearer token, and the browser never attaches
/// it automatically. What remains is defence against a hostile page issuing "simple" or
/// scripted cross-origin requests to the tunnel URL. This middleware closes that gap on every
/// state-mutating (non-GET/HEAD/OPTIONS) request by requiring all of:
/// </para>
/// <list type="number">
///   <item>An <c>Origin</c> (or, as a fallback, <c>Referer</c>) that matches the active tunnel
///   URL or localhost. Cross-origin attacker pages fail this check.</item>
///   <item><c>Content-Type: application/json</c>. A browser cannot set this header on a
///   "simple" cross-origin request without triggering a CORS preflight, which our CORS policy
///   would then reject for an untrusted origin.</item>
///   <item><c>X-Requested-With: XMLHttpRequest</c>, a secondary custom-header check that likewise
///   cannot be forged on a simple cross-origin request.</item>
/// </list>
/// <para>
/// The middleware only runs when the backend is in internet mode (an active Cloudflare tunnel).
/// LAN sessions are same-origin on a trusted network and are not subject to these checks. It
/// always emits <c>Vary: Origin</c> so caches never serve an origin-specific response to a
/// different origin.
/// </para>
/// </summary>
public sealed class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Trace,
    };

    private const string RequestedWithHeader = "X-Requested-With";
    private const string RequestedWithValue = "XMLHttpRequest";

    private readonly RequestDelegate _next;
    private readonly ITunnelStateService _tunnelState;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;

    public CsrfProtectionMiddleware(
        RequestDelegate next,
        ITunnelStateService tunnelState,
        ILogger<CsrfProtectionMiddleware> logger)
    {
        _next = next;
        _tunnelState = tunnelState;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Always advertise that responses vary by Origin so shared caches never mix them up.
        context.Response.Headers.Append("Vary", "Origin");

        // Only enforce in internet mode; LAN sessions are trusted same-origin traffic.
        if (!_tunnelState.IsInternetModeActive)
        {
            await _next(context);
            return;
        }

        // Only state-mutating verbs are guarded; safe/idempotent reads pass through.
        if (SafeMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var tunnelOrigin = _tunnelState.TunnelUrl;

        // 1. Origin / Referer must match the tunnel URL or localhost.
        if (!IsTrustedRequestOrigin(context.Request, tunnelOrigin, out var offendingOrigin))
        {
            await RejectAsync(context, "origin",
                $"Request origin '{offendingOrigin ?? "(none)"}' is not permitted.");
            return;
        }

        // 2. Content-Type must be application/json.
        if (!IsJsonContentType(context.Request.ContentType))
        {
            await RejectAsync(context, "content-type",
                "State-changing requests must use Content-Type: application/json.");
            return;
        }

        // 3. X-Requested-With must be present as a secondary custom-header check.
        var requestedWith = context.Request.Headers[RequestedWithHeader].ToString();
        if (!string.Equals(requestedWith, RequestedWithValue, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(context, "x-requested-with",
                $"Missing or invalid {RequestedWithHeader} header.");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// True when the request's Origin (preferred) or Referer (fallback) matches the active
    /// tunnel origin or a localhost origin. A request with no Origin and no Referer on a
    /// non-safe method is treated as untrusted.
    /// </summary>
    private static bool IsTrustedRequestOrigin(
        HttpRequest request,
        string? tunnelOrigin,
        out string? offendingOrigin)
    {
        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            offendingOrigin = origin;
            return IsAllowedOrigin(origin, tunnelOrigin);
        }

        // Fall back to Referer when Origin is absent.
        var referer = request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer)
            && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var refererOrigin = refererUri.GetLeftPart(UriPartial.Authority);
            offendingOrigin = refererOrigin;
            return IsAllowedOrigin(refererOrigin, tunnelOrigin);
        }

        offendingOrigin = null;
        return false;
    }

    private static bool IsAllowedOrigin(string origin, string? tunnelOrigin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        // localhost / loopback is always allowed (the desktop wrapper and same-machine host).
        if (originUri.IsLoopback)
            return true;

        // Otherwise it must exactly match the active tunnel origin.
        if (string.IsNullOrWhiteSpace(tunnelOrigin))
            return false;

        var normalizedOrigin = originUri.GetLeftPart(UriPartial.Authority);
        return string.Equals(normalizedOrigin, tunnelOrigin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        // Tolerate charset / boundary parameters, e.g. "application/json; charset=utf-8".
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RejectAsync(HttpContext context, string reason, string detail)
    {
        _logger.LogWarning(
            "CSRF check failed ({Reason}). Method={Method}, Path={Path}, Origin={Origin}, RemoteIp={RemoteIp}",
            reason,
            context.Request.Method,
            context.Request.Path.Value,
            context.Request.Headers.Origin.ToString(),
            context.Connection.RemoteIpAddress);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            title = "Cross-origin request rejected",
            status = StatusCodes.Status403Forbidden,
            detail,
        });
    }
}

/// <summary>
/// Extension methods for registering the <see cref="CsrfProtectionMiddleware"/>.
/// </summary>
public static class CsrfProtectionMiddlewareExtensions
{
    /// <summary>
    /// Adds SPA-oriented CSRF protection that is active only in internet mode (an active
    /// Cloudflare tunnel). See <see cref="CsrfProtectionMiddleware"/> for the checks applied.
    /// </summary>
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CsrfProtectionMiddleware>();
    }
}
