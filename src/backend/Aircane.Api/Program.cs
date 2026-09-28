using System.Text;
using Aircane.Api.Authorization;
using Aircane.Api.Health;
using Aircane.Api.Hubs;
using Aircane.Api.Middleware;
using Aircane.Api.RateLimiting;
using Aircane.Application;
using Aircane.Application.Abstractions;
using Aircane.Infrastructure;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// SignalR
builder.Services.AddSignalR();

// Register LibraryHubNotifier so Infrastructure jobs can send hub events
builder.Services.AddScoped<ILibraryHubNotifier, LibraryHubNotifier>();

// Register SessionHubNotifier so application/infrastructure services can broadcast session events
builder.Services.AddScoped<ISessionHubNotifier, SessionHubNotifier>();

// Configure CORS for development
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure JWT Bearer authentication for MVP session tokens.
// The signing key must be set via environment variable (Jwt__SigningKey) or user secrets.
// The development key in appsettings.Development.json is for local dev only - never use it in production.
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (!string.IsNullOrWhiteSpace(jwtSigningKey))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
                ValidateIssuer = true,
                ValidIssuer = "aircane",
                ValidateAudience = true,
                ValidAudience = "aircane",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            // Allow SignalR connections to pass the token as a query parameter (?access_token=...)
            // because browsers cannot set Authorization headers on WebSocket connections.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },

                // Enforce token revocation on every [Authorize] request and hub connection, not
                // just the manual reconnect path. Checks the in-memory session fast path and the
                // persistent per-token store so a revoked token is rejected even after a restart.
                OnTokenValidated = async context =>
                {
                    var revocation = context.HttpContext.RequestServices
                        .GetRequiredService<ITokenRevocationService>();

                    var sessionIdClaim = context.Principal?.FindFirst("sid")?.Value;
                    if (Guid.TryParse(sessionIdClaim, out var sessionId)
                        && revocation.IsSessionRevoked(sessionId))
                    {
                        context.Fail("Session has been revoked.");
                        return;
                    }

                    var jti = context.Principal?.FindFirst(
                        System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                    if (!string.IsNullOrEmpty(jti)
                        && await revocation.IsTokenRevokedAsync(jti, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Token has been revoked.");
                    }
                },
            };
        });
}
else
{
    // No signing key configured - add a no-op authentication scheme so the pipeline doesn't break.
    // This allows the app to start without auth in environments where it hasn't been configured yet.
    builder.Services.AddAuthentication();
}

// Register participant-based authorization policies (HostOnly, DmOrHost, Authenticated).
// In Development, policies are permissive to allow local UI testing without tokens.
// The local-first desktop wrapper sets Aircane:TrustLocalHost=true so the single
// local host can drive host-only surfaces (AI settings, session creation) without
// first obtaining a session-scoped token. A shared/hosted deployment must leave it unset.
var trustLocalHost = builder.Configuration.GetValue<bool>("Aircane:TrustLocalHost");
builder.Services.AddParticipantAuthorization(
    isDevelopment: builder.Environment.IsDevelopment(),
    trustLocalHost: trustLocalHost);

// Configure EF Core with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=aircane;Username=aircane;Password=aircane_dev";

builder.Services.AddDbContext<AircaneDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

// Register Infrastructure services (LibraryService, FileStorageService, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

// Register Application-layer services (validators, etc.)
builder.Services.AddApplication();

// Rate limiting (internet-mode hardening). The limiters are no-ops on the LAN and only
// throttle once the host enables the Cloudflare tunnel. See RateLimitingExtensions.
builder.Services.AddAircaneRateLimiting(builder.Configuration);

// Add health checks
builder.Services.AddHealthChecks();

// Register the enhanced health check service for development diagnostics
builder.Services.AddScoped<HealthCheckService>();

// Register the built-in open-content rules seeder (Phase 10). Runs after migrations on startup.
builder.Services.AddScoped<Aircane.Workers.Seeding.BuiltInContentSeeder>();

// Register the built-in game-system-definition seeder (D&D 5e, Freeform, Pathfinder 2e Remaster).
builder.Services.AddScoped<Aircane.Workers.Seeding.GameSystemDefinitionSeeder>();

// Background job worker: dequeues import/scan/re-embed jobs and dispatches them to handlers.
// The queue and handlers themselves are registered by AddInfrastructure.
builder.Services.AddHostedService<Aircane.Workers.BackgroundJobs.BackgroundJobWorker>();

// Periodic cleanup of expired persistent token-revocation records (runs on startup + daily).
builder.Services.AddHostedService<Aircane.Workers.BackgroundJobs.RevokedTokenCleanupService>();

var app = builder.Build();

// Apply EF Core migrations automatically in Development, and also for the
// local-first desktop wrapper (Aircane:TrustLocalHost=true), so the schema (and
// the pgvector extension created by the migrations) is present on first run
// without a separate deploy step. Shared/hosted Production deployments leave the
// flag unset and apply migrations explicitly as a deploy step.
if (app.Environment.IsDevelopment() || trustLocalHost)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        startupLogger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync();
        startupLogger.LogInformation("Database migrations applied successfully.");

        // The migrations run `CREATE EXTENSION vector`. If the extension did not exist
        // when Npgsql first loaded this database's type catalog (e.g. a first-run or a
        // freshly reset database), the data source won't know the `vector` type yet and
        // any write of a Pgvector.Vector value fails with "Cannot resolve 'vector' to a
        // fully qualified datatype name". Reload the type cache now that the extension
        // is guaranteed to exist, so embedding writes (e.g. the content seeder) succeed.
        var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await connection.ReloadTypesAsync();
        startupLogger.LogInformation("Npgsql type cache reloaded (pgvector types registered).");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to apply database migrations on startup.");
        throw;
    }

    // Seed built-in game-system definitions (mechanics). Idempotent; failures don't block startup.
    try
    {
        startupLogger.LogInformation("Seeding built-in game-system definitions...");
        var systemSeeder = scope.ServiceProvider.GetRequiredService<Aircane.Workers.Seeding.GameSystemDefinitionSeeder>();
        await systemSeeder.SeedAsync();
        startupLogger.LogInformation("Built-in game-system definition seeding complete.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to seed built-in game-system definitions on startup.");
    }

    // Seed built-in open-content rules bundles (Phase 10). Idempotent: already-seeded
    // bundles are skipped. A seeding failure must not prevent the app from starting.
    try
    {
        startupLogger.LogInformation("Seeding built-in content bundles...");
        var seeder = scope.ServiceProvider.GetRequiredService<Aircane.Workers.Seeding.BuiltInContentSeeder>();
        await seeder.SeedAsync();
        startupLogger.LogInformation("Built-in content seeding complete.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to seed built-in content on startup.");
    }
}

// Middleware pipeline

// SECURITY: Block direct access to source document files (PDFs, etc.).
// Aircane never serves source documents to clients. Files are read server-side for indexing only.
// This middleware is a defense-in-depth measure - no static file serving is configured for user
// content directories, but this ensures requests are rejected even if a misconfiguration occurs.
app.UseSourceFileAccessBlocker();

app.UseCors("DevelopmentCors");

// Rate limiting sits ahead of auth so abusive traffic is shed before it reaches
// authentication/DB work. The limiters self-disable when not in internet mode.
app.UseRateLimiter();

// CSRF / cross-origin protection for state-mutating requests. Active only in internet
// mode; adds Vary: Origin to all responses. Runs after CORS so preflight is handled first.
app.UseCsrfProtection();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// SignalR hubs - authorization is enforced via [Authorize] attributes on the hub classes.
app.MapHub<LibraryHub>("/hubs/library");
app.MapHub<SessionHub>("/hubs/session");

// Health endpoint - reports API, database, vector search, and AI provider status.
// Routed under /api to match every other backend endpoint and ride the shared /api proxy.
app.MapGet("/api/health", async (HttpContext httpContext, CancellationToken ct) =>
{
    try
    {
        var healthService = httpContext.RequestServices.GetRequiredService<HealthCheckService>();
        var result = await healthService.CheckAllAsync(ct);
        var statusCode = result.Status == "unhealthy" ? 503 : 200;
        return Results.Json(result, statusCode: statusCode);
    }
    catch (Exception ex)
    {
        // If the health service itself can't be resolved or throws, still return a valid response
        // so the frontend knows the backend is reachable (just degraded).
        var logger = httpContext.RequestServices.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Health check failed with exception");
        return Results.Json(new { status = "degraded", message = ex.Message }, statusCode: 200);
    }
})
.WithName("Health")
.WithTags("Health")
.AllowAnonymous();

await app.RunAsync();

// Make Program accessible for integration tests
public partial class Program { }
