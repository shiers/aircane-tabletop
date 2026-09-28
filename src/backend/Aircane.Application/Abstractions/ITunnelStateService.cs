namespace Aircane.Application.Abstractions;

/// <summary>
/// Tracks the process-wide internet-tunnel state for the local-first backend.
/// <para>
/// Aircane runs one backend per host machine. When the host enables internet play, the
/// desktop wrapper starts a Cloudflare Tunnel and reports the public URL to the backend
/// (via <c>POST /api/sessions/tunnel-url</c>). That single signal puts the process into
/// "internet mode", which switches on the security hardening that is unnecessary for a
/// trusted LAN (per-IP rate limiting and cross-origin/CSRF checks) and surfaces the public
/// URL in <c>GET /api/sessions/network-info</c>.
/// </para>
/// <para>
/// The state is intentionally in-memory and non-persistent: the free Cloudflare tunnel
/// issues a fresh URL each session, so a restarted backend has no tunnel until the host
/// re-enables it.
/// </para>
/// </summary>
public interface ITunnelStateService
{
    /// <summary>
    /// True when an internet tunnel is currently active and the backend should apply
    /// internet-mode hardening (rate limiting, CSRF/origin checks).
    /// </summary>
    bool IsInternetModeActive { get; }

    /// <summary>
    /// The current public tunnel URL (e.g. <c>https://xxx.trycloudflare.com</c>), or null
    /// when no tunnel is active.
    /// </summary>
    string? TunnelUrl { get; }

    /// <summary>
    /// Records that a tunnel is active at the given public URL. Puts the process into
    /// internet mode. Called when the desktop wrapper reports a started tunnel.
    /// </summary>
    /// <param name="tunnelUrl">The public tunnel URL. Must be an absolute https URL.</param>
    void SetTunnelActive(string tunnelUrl);

    /// <summary>
    /// Clears the tunnel state, returning the process to LAN-only mode. Called when the
    /// desktop wrapper stops the tunnel.
    /// </summary>
    void ClearTunnel();
}
