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
builder.Services.AddParticipantAuthorization();

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

var app = builder.Build();

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

// Health endpoint - reports API, database, vector search, and AI provider status
app.MapGet("/health", async (HealthCheckService healthService, CancellationToken ct) =>
{
    var result = await healthService.CheckAllAsync(ct);
    var statusCode = result.Status == "unhealthy" ? 503 : 200;
    return Results.Json(result, statusCode: statusCode);
})
.WithName("Health")
.WithTags("Health")
.AllowAnonymous();

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
