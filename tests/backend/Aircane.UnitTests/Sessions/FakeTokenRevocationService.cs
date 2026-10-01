using System.Collections.Concurrent;
using Aircane.Application.Abstractions;

namespace Aircane.UnitTests.Sessions;

/// <summary>
/// In-memory test double for <see cref="ITokenRevocationService"/>.
/// <para>
/// The production <c>TokenRevocationService</c> reaches the database through an
/// <c>IServiceScopeFactory</c>, which is awkward to construct in a pure unit test. This double
/// keeps both the session fast-path set and the persistent per-token set entirely in memory so
/// token-service tests can exercise revocation without a DbContext.
/// </para>
/// </summary>
public sealed class FakeTokenRevocationService : ITokenRevocationService
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _revokedSessions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    public void RevokeSession(Guid sessionId)
        => _revokedSessions[sessionId] = DateTimeOffset.UtcNow;

    public bool IsSessionRevoked(Guid sessionId)
        => _revokedSessions.ContainsKey(sessionId);

    public Task RevokeTokensAsync(
        IEnumerable<RevokedTokenRecord> tokens,
        CancellationToken cancellationToken = default)
    {
        foreach (var t in tokens)
            _revokedTokens[t.TokenId] = t.ExpiresAt;
        return Task.CompletedTask;
    }

    public Task<bool> IsTokenRevokedAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        var revoked = _revokedTokens.TryGetValue(tokenId, out var expires)
            && expires > DateTimeOffset.UtcNow;
        return Task.FromResult(revoked);
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _revokedTokens.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            _revokedTokens.TryRemove(key, out _);
        return Task.FromResult(expired.Count);
    }
}
