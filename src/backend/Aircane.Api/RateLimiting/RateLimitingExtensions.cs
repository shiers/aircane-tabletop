using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Aircane.Application.Abstractions;
using Aircane.Application.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Aircane.Api.RateLimiting;

/// <summary>
/// Wires up ASP.NET Core rate limiting for internet-exposed sessions.
/// <para>
/// Two policies are registered:
/// <list type="bullet">
///   <item><b>join</b> — a strict fixed-window limiter for the join / participant-approval /
///   token endpoints. Prevents brute-forcing invite codes.</item>
///   <item><b>api</b> — a general sliding-window limiter for every other API endpoint.
///   Prevents the API from being hammered from the public internet.</item>
/// </list>
/// Both partition per client IP. Both become no-ops (unlimited) whenever the backend is not
/// in internet mode, so LAN sessions are never throttled — the check is made per request via
/// <see cref="ITunnelStateService.IsInternetModeActive"/>, which flips as the host starts and
/// stops the tunnel.
/// </para>
/// </summary>
public static class RateLimitingExtensions
{
    private const string UnlimitedPartitionKey = "__lan_unlimited__";

    public static IServiceCollection AddAircaneRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new RateLimitingSettings();
        configuration.GetSection(RateLimitingSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;

            // Emit Retry-After and a structured log line whenever a request is rejected.
            options.OnRejected = OnRejectedAsync;

            // ── join / auth: fixed-window per IP ──────────────────────────────────
            // Applied explicitly to sensitive endpoints via [EnableRateLimiting("join")].
            options.AddPolicy(RateLimitingSettings.JoinPolicy, httpContext =>
            {
                var tunnelState = httpContext.RequestServices.GetRequiredService<ITunnelStateService>();
                if (!tunnelState.IsInternetModeActive)
                    return NoLimitPartition();

                var join = settings.Join;
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = join.PermitLimit,
                        Window = TimeSpan.FromSeconds(join.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = join.QueueLimit,
                    });
            });

            // ── feedback: fixed-window per IP, ALWAYS ON ──────────────────────────
            // Applied to the anonymous POST /api/feedback endpoint via
            // [EnableRateLimiting("feedback")]. Unlike join/api, this policy does NOT consult
            // ITunnelStateService: it must throttle in every mode (LAN and internet) because the
            // endpoint is unauthenticated and each permitted request creates a real GitHub issue.
            options.AddPolicy(RateLimitingSettings.FeedbackPolicy, httpContext =>
            {
                var feedback = settings.Feedback;
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = feedback.PermitLimit,
                        Window = TimeSpan.FromSeconds(feedback.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = feedback.QueueLimit,
                    });
            });

            // ── api: sliding-window per IP ────────────────────────────────────────
            // Also registered as a named policy so it can be referenced explicitly, but it
            // is applied to "all other endpoints" via the global limiter below.
            options.AddPolicy(RateLimitingSettings.ApiPolicy, httpContext =>
                ApiPartition(httpContext, settings));

            // Global limiter: the "api" sliding window applies to every endpoint that does
            // not opt into a more specific named policy. A named [EnableRateLimiting] on an
            // endpoint (e.g. "join") takes precedence over this global limiter.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => ApiPartition(httpContext, settings));
        });

        return services;
    }

    /// <summary>
    /// Builds the general "api" sliding-window partition for a request, or an unlimited
    /// partition when internet mode is off (LAN sessions are never throttled).
    /// </summary>
    private static RateLimitPartition<string> ApiPartition(
        HttpContext httpContext,
        RateLimitingSettings settings)
    {
        var tunnelState = httpContext.RequestServices.GetRequiredService<ITunnelStateService>();
        if (!tunnelState.IsInternetModeActive)
            return NoLimitPartition();

        var api = settings.Api;
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = api.PermitLimit,
                Window = TimeSpan.FromSeconds(api.WindowSeconds),
                SegmentsPerWindow = api.SegmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = api.QueueLimit,
            });
    }

    private static ValueTask OnRejectedAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        // Advise clients when they can retry. Prefer the limiter-provided window if present.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Aircane.Api.RateLimiting");

        logger.LogWarning(
            "Rate limit exceeded. RemoteIp={RemoteIp}, Endpoint={Endpoint}, SessionId={SessionId}",
            GetClientIp(httpContext),
            httpContext.Request.Path.Value,
            TryGetSessionId(httpContext));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns an unlimited partition. Used for LAN sessions (internet mode off) so the
    /// limiter is effectively bypassed without special-casing the middleware pipeline.
    /// </summary>
    private static RateLimitPartition<string> NoLimitPartition()
        => RateLimitPartition.GetNoLimiter(UnlimitedPartitionKey);

    /// <summary>
    /// Resolves the client IP used as the rate-limit partition key.
    /// <para>
    /// Behind Cloudflare Tunnel the socket peer is the local <c>cloudflared</c> process, so
    /// the real client IP arrives in <c>CF-Connecting-IP</c> (falling back to the first
    /// <c>X-Forwarded-For</c> entry). We only trust these headers in internet mode; on the
    /// LAN they are ignored in favour of the socket address.
    /// </para>
    /// </summary>
    private static string GetClientIp(HttpContext httpContext)
    {
        var cfIp = httpContext.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cfIp))
            return cfIp.Trim();

        var forwarded = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (first.Length > 0)
                return first[0];
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string? TryGetSessionId(HttpContext httpContext)
    {
        // Session id appears in the route for /api/sessions/{id}/... endpoints.
        if (httpContext.Request.RouteValues.TryGetValue("id", out var id) && id is not null)
            return id.ToString();
        return null;
    }
}
