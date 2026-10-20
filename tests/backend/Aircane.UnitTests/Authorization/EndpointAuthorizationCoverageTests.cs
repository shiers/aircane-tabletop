using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Aircane.UnitTests.Authorization;

/// <summary>
/// Every controller action and hub must state its authorization: an <c>[Authorize]</c> policy on the
/// action or its class, or an explicit <c>[AllowAnonymous]</c> that is on the allowlist below. The
/// fallback policy already fails closed for anything missed; this test makes the intent explicit
/// and stops a new anonymous endpoint slipping in unreviewed.
/// </summary>
public sealed class EndpointAuthorizationCoverageTests
{
    /// <summary>The endpoints that are meant to work without a token. Adding one is a security review.</summary>
    private static readonly HashSet<string> AnonymousAllowlist =
    [
        "FeedbackController.Submit",
        "LicensesController.ListLicenses",
        "SessionsController.GetJoinInfo",
        "SessionsController.JoinSession",
        "SessionsController.GetNetworkInfo",
        // A player who lost their token proves the seat with the rejoin secret (128+ random bits, stored hashed,
        // rate limited in every mode); unknown secrets, ended and unknown sessions get one non-revealing 404.
        "SessionsController.Rejoin",
        "SessionsController.GetRejoinStatus",
        "SetupController.GetStatus",
        // Loopback-only, checked in the action (the desktop wrapper reports the tunnel URL).
        "TunnelController.SetTunnelUrl",
        "TunnelController.ClearTunnelUrl",
    ];

    private static readonly Assembly ApiAssembly = typeof(Aircane.Api.Authorization.AuthorizationPolicies).Assembly;

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        from controller in ApiAssembly.GetTypes()
        where typeof(ControllerBase).IsAssignableFrom(controller) && !controller.IsAbstract
        from action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        where action.GetCustomAttributes<HttpMethodAttribute>().Any()
        select (controller, action);

    [Fact]
    public void Coverage_FindsTheControllerActions()
    {
        // Guards against the reflection silently matching nothing.
        Assert.True(Actions().Count() > 100, $"Only {Actions().Count()} actions found.");
    }

    [Fact]
    public void EveryControllerAction_DeclaresItsAuthorization()
    {
        var missing = Actions()
            .Where(a => !Declares<IAuthorizeData>(a) && !Declares<IAllowAnonymous>(a))
            .Select(Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "These actions have no [Authorize] or [AllowAnonymous] on the action or its controller: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void AnonymousControllerActions_AreOnTheAllowlist()
    {
        var anonymous = Actions().Where(Declares<IAllowAnonymous>).Select(Name).ToHashSet();

        var unexpected = anonymous.Except(AnonymousAllowlist).ToList();
        Assert.True(unexpected.Count == 0,
            "These actions allow anonymous access but are not on the allowlist: " + string.Join(", ", unexpected));

        // A stale allowlist entry (renamed or removed action) should be cleaned up, not linger.
        var stale = AnonymousAllowlist.Except(anonymous).ToList();
        Assert.True(stale.Count == 0, "Allowlisted but no longer anonymous (or renamed): " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryHub_RequiresAuthorization()
    {
        var hubs = ApiAssembly.GetTypes().Where(t => typeof(Hub).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        Assert.NotEmpty(hubs);
        Assert.All(hubs, hub =>
        {
            Assert.True(hub.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(), $"{hub.Name} has no [Authorize].");
            Assert.Empty(hub.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        });
    }

    private static bool Declares<T>((Type Controller, MethodInfo Action) a) where T : class
        => a.Action.GetCustomAttributes(inherit: true).OfType<T>().Any()
           || a.Controller.GetCustomAttributes(inherit: true).OfType<T>().Any();

    private static string Name((Type Controller, MethodInfo Action) a) => $"{a.Controller.Name}.{a.Action.Name}";
}
