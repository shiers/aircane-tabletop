using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;

namespace Aircane.Api.Authorization;

/// <summary>
/// Desktop-mode requirement: met by the trusted local host (<see cref="LocalHostTrust"/>) without a
/// token, or, like the normal policies, by an authenticated participant holding one of
/// <see cref="AllowedRoles"/>. No roles means any authenticated participant.
/// </summary>
public sealed class LocalHostOrRoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedRoles { get; }

    public LocalHostOrRoleRequirement(params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}

/// <summary>Handles <see cref="LocalHostOrRoleRequirement"/>.</summary>
public sealed class LocalHostOrRoleAuthorizationHandler : AuthorizationHandler<LocalHostOrRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        LocalHostOrRoleRequirement requirement)
    {
        if (LocalHostTrust.IsTrustedLocalCaller(HttpContextOf(context.Resource)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && (requirement.AllowedRoles.Count == 0
                || requirement.AllowedRoles.Contains(user.FindFirst(ClaimTypes.Role)?.Value, StringComparer.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The request being authorized: endpoint routing passes the <see cref="HttpContext"/>, SignalR
    /// hub method checks pass a <see cref="HubInvocationContext"/>. Anything else is not trusted.
    /// </summary>
    private static HttpContext? HttpContextOf(object? resource) => resource switch
    {
        HttpContext httpContext => httpContext,
        HubInvocationContext hub => hub.Context.GetHttpContext(),
        AuthorizationFilterContext filter => filter.HttpContext,
        _ => null,
    };
}
