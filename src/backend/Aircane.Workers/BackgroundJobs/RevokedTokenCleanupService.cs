using Aircane.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aircane.Workers.BackgroundJobs;

/// <summary>
/// Hosted service that keeps the persistent token-revocation table bounded by deleting rows
/// whose expiry has passed. Once a token's <c>ExpiresAt</c> is in the past the underlying JWT is
/// rejected by lifetime validation anyway, so the revocation record is no longer needed.
/// <para>
/// Runs once shortly after startup, then on a daily cadence. The work is a single indexed
/// delete, so it is cheap and safe to run in the background alongside normal request handling.
/// </para>
/// </summary>
public sealed class RevokedTokenCleanupService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RevokedTokenCleanupService> _logger;

    public RevokedTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<RevokedTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RevokedTokenCleanupService started.");

        // Small delay on startup so migrations/seeding finish before the first sweep.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await PurgeOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var revocation = scope.ServiceProvider.GetRequiredService<ITokenRevocationService>();
            var removed = await revocation.PurgeExpiredAsync(cancellationToken);

            if (removed > 0)
                _logger.LogInformation("Revoked-token cleanup removed {Count} expired record(s).", removed);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; ignore.
        }
        catch (Exception ex)
        {
            // A failed sweep must never crash the host; the next run will retry.
            _logger.LogError(ex, "Revoked-token cleanup sweep failed.");
        }
    }
}
