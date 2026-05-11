using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Api.Health;

/// <summary>
/// Provides a development health check that reports the status of the API,
/// database connectivity, vector search availability, and AI provider.
/// </summary>
public sealed class HealthCheckService
{
    private readonly AircaneDbContext _db;
    private readonly IAiProvider _aiProvider;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(
        AircaneDbContext db,
        IAiProvider aiProvider,
        IEmbeddingProvider embeddingProvider,
        ILogger<HealthCheckService> logger)
    {
        _db = db;
        _aiProvider = aiProvider;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs all health checks and returns a composite result.
    /// </summary>
    public async Task<HealthCheckResult> CheckAllAsync(CancellationToken ct = default)
    {
        var api = new ComponentHealth("healthy", "API is running");
        var database = await CheckDatabaseAsync(ct);
        var vectorSearch = await CheckVectorSearchAsync(ct);
        var aiProvider = CheckAiProvider();

        var overallStatus = (database.Status == "healthy" && vectorSearch.Status == "healthy")
            ? "healthy"
            : "degraded";

        var result = new HealthCheckResult(overallStatus, api, database, vectorSearch, aiProvider);

        _logger.LogInformation(
            "Health check completed. Status={Status}, Database={DbStatus}, VectorSearch={VectorStatus}, AiProvider={AiStatus}",
            overallStatus, database.Status, vectorSearch.Status, aiProvider.Status);

        return result;
    }

    private async Task<ComponentHealth> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return new ComponentHealth("healthy", "PostgreSQL connection successful");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
            return new ComponentHealth("unhealthy", $"PostgreSQL connection failed: {ex.Message}");
        }
    }

    private async Task<ComponentHealth> CheckVectorSearchAsync(CancellationToken ct)
    {
        try
        {
            // Check if pgvector extension is available
            var result = await _db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM pg_extension WHERE extname = 'vector'", ct);

            var embeddingStatus = _embeddingProvider.ProviderName;
            var isFake = string.Equals(embeddingStatus, "Fake", StringComparison.OrdinalIgnoreCase);

            return result > 0
                ? new ComponentHealth(
                    isFake ? "degraded" : "healthy",
                    $"pgvector extension available, embedding provider: {embeddingStatus}")
                : new ComponentHealth("degraded", "pgvector extension not installed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search health check failed");
            return new ComponentHealth("unhealthy", $"Vector search check failed: {ex.Message}");
        }
    }

    private ComponentHealth CheckAiProvider()
    {
        try
        {
            var providerName = _aiProvider.ProviderName;
            if (string.Equals(providerName, "Fake", StringComparison.OrdinalIgnoreCase))
            {
                return new ComponentHealth("degraded", "Using fake AI provider (no real AI configured)");
            }

            return new ComponentHealth("healthy", $"AI provider configured: {providerName}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI provider health check failed");
            return new ComponentHealth("unhealthy", $"AI provider check failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Represents the health status of a single component.
/// </summary>
public sealed record ComponentHealth(string Status, string Message);

/// <summary>
/// Composite health check result for all system components.
/// </summary>
public sealed record HealthCheckResult(
    string Status,
    ComponentHealth Api,
    ComponentHealth Database,
    ComponentHealth VectorSearch,
    ComponentHealth AiProvider);
