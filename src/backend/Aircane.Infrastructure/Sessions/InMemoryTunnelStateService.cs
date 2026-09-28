using Aircane.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Sessions;

/// <summary>
/// In-memory implementation of <see cref="ITunnelStateService"/>.
/// <para>
/// Registered as a singleton so the tunnel state is shared across every request and hub
/// connection for the lifetime of the process. The state is deliberately not persisted:
/// the free Cloudflare tunnel issues a new URL each session, so a restarted backend starts
/// in LAN-only mode until the host re-enables internet play.
/// </para>
/// </summary>
public sealed class InMemoryTunnelStateService : ITunnelStateService
{
    private readonly ILogger<InMemoryTunnelStateService> _logger;
    private readonly object _gate = new();

    private string? _tunnelUrl;

    public InMemoryTunnelStateService(ILogger<InMemoryTunnelStateService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsInternetModeActive
    {
        get
        {
            lock (_gate)
            {
                return _tunnelUrl is not null;
            }
        }
    }

    /// <inheritdoc />
    public string? TunnelUrl
    {
        get
        {
            lock (_gate)
            {
                return _tunnelUrl;
            }
        }
    }

    /// <inheritdoc />
    public void SetTunnelActive(string tunnelUrl)
    {
        if (string.IsNullOrWhiteSpace(tunnelUrl))
            throw new ArgumentException("Tunnel URL must be provided.", nameof(tunnelUrl));

        if (!Uri.TryCreate(tunnelUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Tunnel URL must be an absolute https URL.", nameof(tunnelUrl));
        }

        // Normalise to origin (scheme + host + optional port) so downstream origin checks
        // compare like-for-like and never trip over a trailing path or slash.
        var normalized = uri.GetLeftPart(UriPartial.Authority);

        lock (_gate)
        {
            _tunnelUrl = normalized;
        }

        _logger.LogInformation(
            "Internet mode enabled. Tunnel active at {TunnelUrl}", normalized);
    }

    /// <inheritdoc />
    public void ClearTunnel()
    {
        lock (_gate)
        {
            if (_tunnelUrl is null)
                return;
            _tunnelUrl = null;
        }

        _logger.LogInformation("Internet mode disabled. Tunnel cleared.");
    }
}
