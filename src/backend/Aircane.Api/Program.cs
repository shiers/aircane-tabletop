using System.Text;
using Aircane.Api.Authorization;
using Aircane.Api.Health;
using Aircane.Api.Hubs;
using Aircane.Api.Middleware;
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
builder.Services.AddParticipantAuthorization(isDevelopment: builder.Environment.IsDevelopment());

// Configure EF Core with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=aircane;Username=aircane;Password=aircane_dev";

builder.Services.AddDbContext<AircaneDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

// Register Infrastructure services (LibraryService, FileStorageService, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

// Register Application-layer services (validators, etc.)
builder.Services.AddApplication();

// Add health checks
builder.Services.AddHealthChecks();

// Register the enhanced health check service for development diagnostics
builder.Services.AddScoped<HealthCheckService>();

// Register the built-in open-content rules seeder (Phase 10). Runs after migrations on startup.
builder.Services.AddScoped<Aircane.Workers.Seeding.BuiltInContentSeeder>();

// The built-in license/attribution text (OGL-1.0a.txt, SECTION-15.txt) ships as embedded
// resources in the Aircane.Workers assembly. Bind the reader to that assembly here, where the
// Workers reference is available, so Infrastructure need not depend on Workers.
builder.Services.AddScoped<Aircane.Application.Abstractions.IBuiltInLicenseTextReader>(sp =>
    new Aircane.Infrastructure.DocumentSources.BuiltInLicenseTextReader(
        typeof(Aircane.Workers.Seeding.BuiltInContentSeeder).Assembly,
        sp.GetRequiredService<ILogger<Aircane.Infrastructure.DocumentSources.EmbeddedResourceDocumentSource>>()));

var app = builder.Build();

// Apply EF Core migrations automatically in Development so the schema (and the
// pgvector extension created by the migrations) is present on first run.
// Production deployments should apply migrations explicitly as a deploy step.
if (app.Environment.IsDevelopment())
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
