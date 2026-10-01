using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Aircane.Api.Authorization;

/// <summary>
/// Authorization requirement that checks whether the authenticated participant
/// holds one of the allowed roles.
/// </summary>
public sealed class ParticipantRoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedRoles { get; }

    public ParticipantRoleRequirement(params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}

/// <summary>
/// Handles <see cref="ParticipantRoleRequirement"/> by checking the role claim
/// in the authenticated participant's token.
/// </summary>
public sealed class ParticipantRoleAuthorizationHandler
    : AuthorizationHandler<ParticipantRoleRequirement>
{
    private readonly ILogger<ParticipantRoleAuthorizationHandler> _logger;

    public ParticipantRoleAuthorizationHandler(ILogger<ParticipantRoleAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ParticipantRoleRequirement requirement)
    {
        var user = context.User;

        if (user.Identity is null || !user.Identity.IsAuthenticated)
        {
            _logger.LogWarning("Authorization failed: user is not authenticated. RequiredRoles=[{AllowedRoles}]",
                string.Join(", ", requirement.AllowedRoles));
            return Task.CompletedTask;
        }

        // The role claim is stored as ClaimTypes.Role by ParticipantTokenService.
        var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrEmpty(roleClaim))
        {
            _logger.LogWarning("Authorization failed: no role claim found in token.");
            return Task.CompletedTask;
        }

        if (requirement.AllowedRoles.Contains(roleClaim, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation(
                "Authorization denied: participant role '{Role}' is not in allowed roles [{AllowedRoles}].",
                roleClaim, string.Join(", ", requirement.AllowedRoles));
        }

        return Task.CompletedTask;
    }
}
