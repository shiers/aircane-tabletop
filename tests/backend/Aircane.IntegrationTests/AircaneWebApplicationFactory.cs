using System.Security.Claims;
using System.Text.Encodings.Web;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aircane.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that:
/// - Replaces PostgreSQL with an in-memory database
/// - Uses fake AI and embedding providers (already default when no config is set)
/// - Bypasses authentication with a test scheme that always succeeds as Host
/// - Provides a test JWT signing key for ParticipantTokenService
/// </summary>
public class AircaneWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName;

    // Single public constructor: xUnit's IClassFixture requires exactly one. Tests that need a
    // specific (shareable) database name use the SharedDbWebApplicationFactory subclass, which
    // passes the name through the protected constructor.
    public AircaneWebApplicationFactory()
        : this($"AircaneTestDb_{Guid.NewGuid()}")
    {
    }

    /// <summary>
    /// Creates a factory bound to a specific in-memory database name. Two factories sharing
    /// the same name share the store, which lets a test simulate a server restart (new process,
    /// same persistent data) — used by the persistent token-revocation tests.
    /// </summary>
    protected AircaneWebApplicationFactory(string dbName)
    {
        _dbName = dbName;
    }

    /// <summary>The in-memory database name this factory uses.</summary>
    public string DatabaseName => _dbName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Provide test configuration values (JWT signing key, etc.)
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "TestSigningKey_AtLeast32Characters_ForHMACSHA256!",
                ["Ai:Provider"] = "Fake",
                ["Embeddings:Provider"] = "Fake",
                ["ConnectionStrings:DefaultConnection"] = null,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AircaneDbContext>));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            // Remove any DbContext-related registrations that reference Npgsql
            var dbContextPoolDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions));
            if (dbContextPoolDescriptor != null)
                services.Remove(dbContextPoolDescriptor);

            // Add in-memory database with a unique name per factory instance
            services.AddDbContext<AircaneDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            // Remove and re-register the ParticipantTokenService singleton so it picks up
            // the test JWT signing key from our in-memory configuration.
            var tokenServiceDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(Aircane.Application.Abstractions.IParticipantTokenService));
            if (tokenServiceDescriptor != null)
                services.Remove(tokenServiceDescriptor);
            services.AddSingleton<Aircane.Application.Abstractions.IParticipantTokenService,
                Aircane.Infrastructure.Sessions.ParticipantTokenService>();

            // Replace authentication with a test scheme that always authenticates as Host
            services.AddAuthentication("TestScheme")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "TestScheme", options => { });

            // Override the default authentication scheme
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "TestScheme";
                options.DefaultChallengeScheme = "TestScheme";
            });
        });
    }
}

/// <summary>
/// A factory bound to a caller-supplied in-memory database name. Two instances created with the
/// same name share the underlying store, letting a test simulate a server restart (a fresh host
/// process whose in-memory revocation set is empty, but whose persistent RevokedTokens rows
/// survive). Has a single public constructor so it remains usable as a plain factory.
/// </summary>
public sealed class SharedDbWebApplicationFactory : AircaneWebApplicationFactory
{
    public SharedDbWebApplicationFactory(string dbName) : base(dbName)
    {
    }
}

/// <summary>
/// Authentication handler that always succeeds with a Host role claim.
/// This allows integration tests to bypass JWT validation.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "TestHost"),
            new Claim("participantId", Guid.NewGuid().ToString()),
            new Claim("sessionId", Guid.Empty.ToString()),
            new Claim("role", "Host"),
            new Claim(ClaimTypes.Role, "Host"),
        };

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
