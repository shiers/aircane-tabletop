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
        bool isDevelopment = false,
        bool trustLocalHost = false)
    {
        services.AddSingleton<IAuthorizationHandler, ParticipantRoleAuthorizationHandler>();

        // "Trust local host" makes the host-only policies permissive, exactly like
        // Development. It exists for the local-first desktop wrapper: the single
        // user running the app on their own machine IS the host, so requiring a
        // session-scoped Host token before they can create a session is a
        // chicken-and-egg problem. The desktop sidecar opts in explicitly via
        // Aircane:TrustLocalHost=true; a shared/hosted deployment must NOT set it.
        if (isDevelopment || trustLocalHost)
        {
            // Allow all requests through without authentication so the local host
            // can drive host-only surfaces without first joining a session.
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
