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
    /// In desktop mode and in Development the local host is trusted without a token (see below); there
    /// is no mode in which a remote caller is. In every mode an endpoint with no authorization attribute falls back to
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
        // Scoped: it reads the roster and resolves campaigns, characters, runs and proposals to their session.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, SessionScopeAuthorizationHandler>());
        // Replace, not TryAdd: AddAuthorization has already registered the default one.
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, SessionScopeResultHandler>());

        // "Trust local host" exists for the local-first desktop wrapper: the single user running
        // the app on their own machine IS the host, so requiring a session-scoped Host token before
        // they can create a session is a chicken-and-egg problem. The desktop sidecar opts in
        // explicitly via Aircane:TrustLocalHost=true; a shared/hosted deployment must NOT set it.
        // A developer running `dotnet run` (the Development environment) gets the same convenience.
        //
        // The trust is for that user only. The backend can bind every interface (LAN players connect
        // to it; docs/setup/lan-hosting.md tells developers to), and the Cloudflare tunnel and the
        // Vite dev proxy forward other people's traffic from 127.0.0.1, so these policies are the
        // normal role rules plus a shortcut for a direct request from this machine (LocalHostTrust).
        // Everyone else needs a participant token, exactly as in a hosted deployment.
        if (trustLocalHost || isDevelopment)
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
                .AddPolicy(AuthorizationPolicies.SessionMember, policy =>
                {
                    policy.AddRequirements(new LocalHostOrRoleRequirement());
                    policy.AddRequirements(new SessionScopeRequirement(trustLocalHost: true));
                })
                .SetFallbackPolicy(authenticated);
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
                .AddPolicy(AuthorizationPolicies.SessionMember, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new SessionScopeRequirement(trustLocalHost: false));
                })
                .SetFallbackPolicy(authenticated);
        }

        return services;
    }
}
