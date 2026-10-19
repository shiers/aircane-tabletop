using System.Net;
using System.Text;
using Aircane.Api.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Aircane.IntegrationTests;

/// <summary>
/// The backend as the desktop app runs it: <c>Aircane:TrustLocalHost</c> authorization, real JWT
/// participant tokens (not the base factory's always-Host test scheme), and a caller address the
/// test chooses.
/// <para>
/// Desktop mode is switched on by registering the authorization policies with
/// <c>trustLocalHost: true</c> after <c>Program</c> has registered its own (a later policy of the
/// same name replaces the earlier one), rather than by setting the config flag: the flag also runs
/// the startup database migrations, which the in-memory test database cannot apply.
/// </para>
/// </summary>
public class DesktopModeWebApplicationFactory : AircaneWebApplicationFactory
{
    /// <summary>
    /// False to run as the desktop app does when no Jwt:SigningKey is set: no participant token can
    /// be validated, and the app's no-credentials scheme is the default.
    /// </summary>
    protected virtual bool WithParticipantTokens => true;

    /// <summary>
    /// Registers the authorization policies as a Development environment would. Development itself
    /// cannot be selected here: it also runs the startup database migrations, which the in-memory
    /// test database cannot apply.
    /// </summary>
    protected virtual bool DevelopmentMode => false;

    /// <summary>Registers the policies with <c>Aircane:TrustLocalHost</c> (desktop mode).</summary>
    protected virtual bool TrustLocalHostMode => true;

    /// <summary>Test-only header: the remote address the request should appear to come from.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    // Must match the Jwt:SigningKey the base factory configures for ParticipantTokenService.
    private const string TestSigningKey = "TestSigningKey_AtLeast32Characters_ForHMACSHA256!";

    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "aircane-desktop-auth-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>A client whose requests arrive from <paramref name="remoteIp"/>.</summary>
    public HttpClient CreateClientFrom(IPAddress remoteIp)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(RemoteIpHeader, remoteIp.ToString());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Keep the AI settings and documents of this test away from the developer's real data
        // directory (whose ai-settings.json the build copies into the test output).
        Directory.CreateDirectory(Path.Combine(_dataDir, "documents"));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Storage:DocumentsPath"] = Path.Combine(_dataDir, "documents") }));

        builder.ConfigureServices(services =>
        {
            services.AddParticipantAuthorization(isDevelopment: DevelopmentMode, trustLocalHost: TrustLocalHostMode);

            // Program registers the JWT bearer scheme only when it sees Jwt:SigningKey, which it
            // reads before the factory's configuration is applied, so register it here with the
            // same validation parameters and the key the participant token service signs with.
            if (!WithParticipantTokens)
            {
                // Program saw no signing key (see above) and registered the no-credentials scheme.
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = NoCredentialsAuthenticationHandler.SchemeName;
                    options.DefaultAuthenticateScheme = NoCredentialsAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = NoCredentialsAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = NoCredentialsAuthenticationHandler.SchemeName;
                });
                services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
                return;
            }

            services.AddAuthentication().AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = "aircane",
                    ValidateAudience = true,
                    ValidAudience = "aircane",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // The events Program gives the scheme: per-request revocation, and the clear refusal for
                // a participant the host denied.
                options.Events = ParticipantJwtEvents.Create();
            });

            // Undo the base factory's always-Host test scheme: authenticate with the app's own
            // JWT bearer scheme, so a request is anonymous unless it carries a real token.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            });

            services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { Directory.Delete(_dataDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Sets the connection's remote address from <see cref="RemoteIpHeader"/>, first in the pipeline.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var value)
                    && IPAddress.TryParse(value.ToString(), out var ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }
                context.Request.Headers.Remove(RemoteIpHeader);
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>Desktop mode with no JWT signing key configured, as the sidecar runs by default.</summary>
public sealed class DesktopModeWithoutSigningKeyFactory : DesktopModeWebApplicationFactory
{
    protected override bool WithParticipantTokens => false;
}

/// <summary>
/// The backend as a developer runs it with <c>dotnet run</c>: the Development environment, no
/// <c>Aircane:TrustLocalHost</c>. docs/setup/lan-hosting.md has people add
/// <c>--urls http://0.0.0.0:5000</c>, so LAN devices can reach it.
/// </summary>
public sealed class DevelopmentModeWebApplicationFactory : DesktopModeWebApplicationFactory
{
    protected override bool DevelopmentMode => true;
    protected override bool TrustLocalHostMode => false;
}
