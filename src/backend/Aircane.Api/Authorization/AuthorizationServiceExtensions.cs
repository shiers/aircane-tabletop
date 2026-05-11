using Microsoft.AspNetCore.Authorization;

namespace Aircane.Api.Authorization;

/// <summary>
/// Extension methods to register authorization policies and handlers.
/// </summary>
public static class AuthorizationServiceExtensions
{
    /// <summary>
    /// Adds the participant-based authorization policies and the custom handler.
    /// </summary>
    public static IServiceCollection AddParticipantAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, ParticipantRoleAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.HostOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ParticipantRoleRequirement("Host"));
            })
            .AddPolicy(AuthorizationPolicies.DmOrHost, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ParticipantRoleRequirement("Host", "HumanDm"));
            })
            .AddPolicy(AuthorizationPolicies.Authenticated, policy =>
            {
                policy.RequireAuthenticatedUser();
            });

        return services;
    }
}
