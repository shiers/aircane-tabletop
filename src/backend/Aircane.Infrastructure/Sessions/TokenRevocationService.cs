using System.Collections.Concurrent;
using Aircane.Application.Abstractions;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Sessions;

/// <summary>
/// Two-layer token revocation: an in-memory session set for a cheap fast-path check, backed by
/// the persistent <c>RevokedTokens</c> table which is authoritative across process restarts.
/// <para>
/// Registered as a singleton so the in-memory set survives request lifetimes. Because the
/// persistent operations need a scoped <see cref="AircaneDbContext"/>, this singleton creates a
/// short-lived DI scope per database call via <see cref="IServiceScopeFactory"/>.
/// </para>
/// </summary>
public sealed class TokenRevocationService : ITokenRevocationService
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _revokedSessions = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenRevocationService> _logger;

    public TokenRevocationService(
        IServiceScopeFactory scopeFactory,
        ILogger<TokenRevocationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

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

    /// <inheritdoc />
    public async Task RevokeTokensAsync(
        IEnumerable<RevokedTokenRecord> tokens,
        CancellationToken cancellationToken = default)
    {
        var records = tokens
            .Where(t => !string.IsNullOrWhiteSpace(t.TokenId))
            .Select(t => new RevokedToken(t.TokenId, t.SessionId, t.ExpiresAt))
            .ToList();

        if (records.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();

        db.RevokedTokens.AddRange(records);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted {Count} token revocation(s) for session(s) {SessionIds}.",
            records.Count,
            string.Join(",", records.Select(r => r.SessionId).Distinct()));
    }

    /// <inheritdoc />
    public async Task<bool> IsTokenRevokedAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();

        var now = DateTimeOffset.UtcNow;
        return await db.RevokedTokens
            .AsNoTracking()
            .AnyAsync(r => r.TokenId == tokenId && r.ExpiresAt > now, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();

        var now = DateTimeOffset.UtcNow;
        var expired = await db.RevokedTokens
            .Where(r => r.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        db.RevokedTokens.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Purged {Count} expired token revocation record(s).", expired.Count);
        return expired.Count;
    }
}
