namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Network information for reaching this host, used by the desktop wrapper to
/// show the local and LAN join URLs (window title, tray menu, QR code) and by
/// the in-app session screen.
/// </summary>
/// <param name="LocalUrl">
/// Loopback URL for the host machine itself, e.g. <c>http://localhost:5000</c>.
/// </param>
/// <param name="LanUrl">
/// URL other devices on the same local network use to reach this host, e.g.
/// <c>http://192.168.1.42:5000</c>. Null when no non-loopback IPv4 address could
/// be determined (e.g. the machine is offline).
/// </param>
/// <param name="InviteCode">
/// The plaintext invite code for the active session, when one is known to the
/// caller. Always null from this endpoint: invite codes are returned only once,
/// at session creation, and are never persisted in plaintext.
/// </param>
public sealed record NetworkInfoDto(
    string LocalUrl,
    string? LanUrl,
    string? InviteCode);
