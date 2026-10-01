namespace Aircane.Application.Configuration;

/// <summary>
/// Configurable request throttling limits, applied only when the backend is in internet
/// mode (an active Cloudflare tunnel). LAN sessions are never rate limited.
/// <para>
/// Bound from the <c>RateLimiting</c> section of configuration so limits can be tuned
/// without a code change. See <see cref="SectionName"/>.
/// </para>
/// </summary>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Policy name for the strict per-IP limiter on join/auth endpoints.</summary>
    public const string JoinPolicy = "join";

    /// <summary>Policy name for the general per-IP limiter on all other API endpoints.</summary>
    public const string ApiPolicy = "api";

    /// <summary>
    /// Policy name for the per-IP limiter on the anonymous feedback endpoint. Unlike
    /// <see cref="JoinPolicy"/>/<see cref="ApiPolicy"/>, this limiter is always on (it is never a
    /// no-op on the LAN) because the endpoint is unauthenticated and creates real GitHub issues.
    /// </summary>
    public const string FeedbackPolicy = "feedback";

    /// <summary>
    /// Fixed-window limits for sensitive endpoints (join, participant approval, and any
    /// endpoint accepting an invite code or token). Prevents brute-forcing invite codes.
    /// </summary>
    public FixedWindowLimit Join { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60,
        QueueLimit = 0,
    };

    /// <summary>
    /// Sliding-window limits for all other API endpoints in internet mode. Prevents the
    /// API from being hammered from the public internet.
    /// </summary>
    public SlidingWindowLimit Api { get; set; } = new()
    {
        PermitLimit = 200,
        WindowSeconds = 60,
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    };

    /// <summary>
    /// Fixed-window limits for the anonymous feedback endpoint. Applied in every mode (LAN and
    /// internet) because the endpoint is unauthenticated and creates real GitHub issues. Defaults
    /// to 5 submissions per IP per hour.
    /// </summary>
    public FixedWindowLimit Feedback { get; set; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 3600,
        QueueLimit = 0,
    };
}

/// <summary>Fixed-window rate limit parameters.</summary>
public sealed class FixedWindowLimit
{
    /// <summary>Maximum requests permitted per window per client IP.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>Window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Requests allowed to queue when the limit is reached (0 = reject immediately).</summary>
    public int QueueLimit { get; set; }
}

/// <summary>Sliding-window rate limit parameters.</summary>
public sealed class SlidingWindowLimit
{
    /// <summary>Maximum requests permitted per window per client IP.</summary>
    public int PermitLimit { get; set; } = 200;

    /// <summary>Window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Number of segments the window is divided into.</summary>
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>Requests allowed to queue when the limit is reached (0 = reject immediately).</summary>
    public int QueueLimit { get; set; }
}
