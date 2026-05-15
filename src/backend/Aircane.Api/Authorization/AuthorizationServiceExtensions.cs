using Microsoft.AspNetCore.Authorization;

namespace Aircane.Api.Authorization;

/// <summary>
/// Extension methods to register authorization policies and handlers.
/// </summary>
public static class AuthorizationServiceExtensions
{
    /// <summary>
    /// Adds the participant-based authorization policies and the custom handler.
    /// In Development environment, policies are permissive to allow local testing without tokens.
    /// </summary>
    public static IServiceCollection AddParticipantAuthorization(
        this IServiceCollection services,
        bool isDevelopment = false)
    {
        services.AddSingleton<IAuthorizationHandler, ParticipantRoleAuthorizationHandler>();

        if (isDevelopment)
        {
            // In development, allow all requests through without authentication.
            // This enables local UI testing without needing to create a session first.
            services.AddAuthorizationBuilder()
                .AddPolicy(AuthorizationPolicies.HostOnly, policy =>
                {
                    policy.RequireAssertion(_ => true);
                })
                .AddPolicy(AuthorizationPolicies.DmOrHost, policy =>
                {
                    policy.RequireAssertion(_ => true);
                })
                .AddPolicy(AuthorizationPolicies.Authenticated, policy =>
                {
                    policy.RequireAssertion(_ => true);
                });
        }
        else
        {
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
        }

        return services;
    }
}
