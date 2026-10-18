using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aircane.Api.Authorization;

/// <summary>
/// Extension methods to register authorization policies and handlers.
/// </summary>
public static class AuthorizationServiceExtensions
{
    /// <summary>
    /// Adds the participant-based authorization policies and the custom handlers.
    /// In desktop mode the local host is trusted without a token (see below); in Development every
    /// policy is permissive. In every mode an endpoint with no authorization attribute falls back to
    /// <see cref="AuthorizationPolicies.Authenticated"/>, so a forgotten attribute fails closed;
    /// anonymous endpoints must say so with <c>[AllowAnonymous]</c>.
    /// </summary>
    public static IServiceCollection AddParticipantAuthorization(
        this IServiceCollection services,
        bool isDevelopment = false,
        bool trustLocalHost = false)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, ParticipantRoleAuthorizationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, LocalHostOrRoleAuthorizationHandler>());

        // Who a token-less caller is, for actions that check the role themselves; matches the policies below.
        services.RemoveAll<AnonymousHostRule>();
        services.AddSingleton(isDevelopment && !trustLocalHost ? new AnonymousHostRule(trustAnyone: true) : AnonymousHostRule.LocalHostOnly);

        // "Trust local host" exists for the local-first desktop wrapper: the single user running
        // the app on their own machine IS the host, so requiring a session-scoped Host token before
        // they can create a session is a chicken-and-egg problem. The desktop sidecar opts in
        // explicitly via Aircane:TrustLocalHost=true; a shared/hosted deployment must NOT set it.
        //
        // The trust is for that user only. The desktop backend binds every interface (LAN players
        // connect to it) and the Cloudflare tunnel delivers internet traffic from 127.0.0.1, so
        // these policies are the normal role rules plus a shortcut for a direct loopback request
        // (LocalHostTrust). Everyone else needs a participant token, exactly as in a hosted
        // deployment. It takes precedence over Development.
        if (trustLocalHost)
        {
            var authenticated = new AuthorizationPolicyBuilder()
                .AddRequirements(new LocalHostOrRoleRequirement())
                .Build();

            services.AddAuthorizationBuilder()
                .AddPolicy(AuthorizationPolicies.HostOnly, policy =>
                    policy.AddRequirements(new LocalHostOrRoleRequirement("Host")))
                .AddPolicy(AuthorizationPolicies.DmOrHost, policy =>
                    policy.AddRequirements(new LocalHostOrRoleRequirement("Host", "HumanDm")))
                .AddPolicy(AuthorizationPolicies.Authenticated, authenticated)
                .SetFallbackPolicy(authenticated);
        }
        else if (isDevelopment)
        {
            // Development stays open so local UI testing works without tokens, including the
            // docker-compose profile, where requests reach the backend from the Docker gateway
            // rather than loopback. Never expose a Development backend to a network you don't trust.
            var allowAll = new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build();

            services.AddAuthorizationBuilder()
                .AddPolicy(AuthorizationPolicies.HostOnly, allowAll)
                .AddPolicy(AuthorizationPolicies.DmOrHost, allowAll)
                .AddPolicy(AuthorizationPolicies.Authenticated, allowAll)
                .SetFallbackPolicy(allowAll);
        }
        else
        {
            var authenticated = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

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
                .AddPolicy(AuthorizationPolicies.Authenticated, authenticated)
                .SetFallbackPolicy(authenticated);
        }

        return services;
    }
}
