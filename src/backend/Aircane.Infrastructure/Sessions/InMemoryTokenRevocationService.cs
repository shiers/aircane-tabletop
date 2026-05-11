using System.Collections.Concurrent;
using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.Sessions;

/// <summary>
/// In-memory token revocation list for the MVP.
/// Tracks which sessions have been ended so that participant tokens can be rejected.
/// This is intentionally a singleton — the revocation set must survive request lifetimes.
/// </summary>
/// <remarks>
/// For internet/cloud mode this should be replaced with a persistent store (e.g. Redis or a DB table).
/// </remarks>
public sealed class InMemoryTokenRevocationService : ITokenRevocationService
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _revokedSessions = new();

    /// <inheritdoc />
    public void RevokeSession(Guid sessionId)
    {
        _revokedSessions[sessionId] = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public bool IsSessionRevoked(Guid sessionId)
    {
        return _revokedSessions.ContainsKey(sessionId);
    }
}
