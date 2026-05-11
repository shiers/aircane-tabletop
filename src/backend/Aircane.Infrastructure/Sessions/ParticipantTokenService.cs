using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Aircane.Infrastructure.Sessions;

/// <summary>
/// Issues and validates HMAC-SHA256 signed participant tokens for MVP session authentication.
/// The signing key is read from configuration (Jwt:SigningKey) — never hard-coded.
/// </summary>
public sealed class ParticipantTokenService : IParticipantTokenService
{
    // Claim type constants — kept short to minimise token size.
    internal const string ClaimSessionId = "sid";
    internal const string ClaimParticipantId = "pid";
    internal const string ClaimDisplayName = "dname";
    internal const string ClaimRole = ClaimTypes.Role;

    private const string TokenIssuer = "aircane";
    private const string TokenAudience = "aircane";

    private readonly ITokenRevocationService _revocation;
    private readonly ILogger<ParticipantTokenService> _logger;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly TimeSpan _tokenExpiry;

    public ParticipantTokenService(
        IConfiguration configuration,
        ITokenRevocationService revocation,
        ILogger<ParticipantTokenService> logger)
    {
        _revocation = revocation;
        _logger = logger;

        var rawKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "JWT signing key is not configured. Set 'Jwt:SigningKey' in environment variables or user secrets.");

        if (rawKey.Length < 32)
            throw new InvalidOperationException(
                "JWT signing key must be at least 32 characters long.");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rawKey));

        // Default to 24 hours; configurable via Jwt:ExpiryHours.
        var expiryHoursStr = configuration["Jwt:ExpiryHours"];
        var expiryHours = double.TryParse(expiryHoursStr, out var parsed) ? parsed : 24.0;
        _tokenExpiry = TimeSpan.FromHours(expiryHours);
    }

    /// <inheritdoc />
    public string IssueToken(Guid sessionId, Guid participantId, string displayName, string role)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(_tokenExpiry);

        var claims = new[]
        {
            new Claim(ClaimSessionId, sessionId.ToString()),
            new Claim(ClaimParticipantId, participantId.ToString()),
            new Claim(ClaimDisplayName, displayName),
            new Claim(ClaimRole, role),
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TokenIssuer,
            audience: TokenAudience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogDebug(
            "Issued participant token for participant {ParticipantId} in session {SessionId}, expires {ExpiresAt}",
            participantId, sessionId, expires);

        return tokenString;
    }

    /// <inheritdoc />
    public ParticipantTokenClaims? ValidateToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = TokenIssuer,
            ValidateAudience = true,
            ValidAudience = TokenAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out _);

            var sessionIdStr = GetClaimValue(principal, ClaimSessionId);
            var participantIdStr = GetClaimValue(principal, ClaimParticipantId);
            var displayName = GetClaimValue(principal, ClaimDisplayName);
            var role = GetClaimValue(principal, ClaimRole);

            if (sessionIdStr is null || participantIdStr is null || displayName is null || role is null)
            {
                _logger.LogWarning("Participant token is missing required claims.");
                return null;
            }

            if (!Guid.TryParse(sessionIdStr, out var sessionId) ||
                !Guid.TryParse(participantIdStr, out var participantId))
            {
                _logger.LogWarning("Participant token contains malformed GUID claims.");
                return null;
            }

            // Check the in-memory revocation list — session ended by host.
            if (_revocation.IsSessionRevoked(sessionId))
            {
                _logger.LogInformation(
                    "Rejected token for revoked session {SessionId}.", sessionId);
                return null;
            }

            return new ParticipantTokenClaims(sessionId, participantId, displayName, role);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Participant token validation failed.");
            return null;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Participant token validation failed due to invalid input.");
            return null;
        }
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
        => principal.FindFirst(claimType)?.Value;
}
