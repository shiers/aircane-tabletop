using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aircane.Api.Authorization;

/// <summary>
/// The default authentication scheme when no JWT signing key is configured, so no participant token
/// can be validated. It never authenticates anyone; it exists so a request the authorization
/// policies reject gets a plain 401 (challenge) or 403 (forbid) instead of an error about a missing
/// challenge scheme. The desktop app runs this way unless <c>Jwt__SigningKey</c> is set, and there
/// only the trusted local host passes the policies.
/// </summary>
public sealed class NoCredentialsAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "NoCredentials";

    public NoCredentialsAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}
