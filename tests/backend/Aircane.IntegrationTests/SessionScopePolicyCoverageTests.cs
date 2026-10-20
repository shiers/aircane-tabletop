using System.Text.RegularExpressions;
using Aircane.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Every endpoint whose route names something that belongs to a session must carry the
/// <see cref="AuthorizationPolicies.SessionMember"/> policy, so a new action on a session-keyed route cannot
/// quietly skip the session check. Read from the running app's endpoint table, so it sees exactly what
/// routing sees (class and action attributes combined).
/// </summary>
public sealed class SessionScopePolicyCoverageTests : IClassFixture<AircaneWebApplicationFactory>
{
    /// <summary>
    /// The only session-keyed endpoints that may be anonymous: a player has no token before joining, or (rejoin)
    /// has lost it and proves the seat with the rejoin secret instead.
    /// </summary>
    private static readonly HashSet<string> AnonymousAllowlist =
    [
        "api/sessions/{id:guid}/join",
        "api/sessions/{id:guid}/join-info",
        "api/sessions/{id:guid}/rejoin",
        "api/sessions/{id:guid}/rejoin-status",
    ];

    // A route parameter that names a session, or something a session owns.
    private static readonly Regex SessionKeyedParameter = new(@"\{(sessionId|campaignId|proposalId)(:[^}]*)?\}", RegexOptions.IgnoreCase);

    // `{id}` directly under a resource that belongs to a session.
    private static readonly Regex SessionKeyedId = new(@"^/?api/(sessions|campaigns|characters|adventure-sessions)/\{id(:[^}]*)?\}", RegexOptions.IgnoreCase);

    private readonly AircaneWebApplicationFactory _factory;

    public SessionScopePolicyCoverageTests(AircaneWebApplicationFactory factory)
    {
        _factory = factory;
        _ = _factory.CreateClient();
    }

    private IReadOnlyList<RouteEndpoint> SessionKeyedEndpoints()
        => _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                        && (SessionKeyedParameter.IsMatch(raw) || SessionKeyedId.IsMatch(raw)))
            .ToList();

    private static string Describe(RouteEndpoint e)
        => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText} ({e.DisplayName})";

    [Fact]
    public void EverySessionKeyedEndpoint_RequiresSessionMember_OrIsAnAllowlistedAnonymousOne()
    {
        var endpoints = SessionKeyedEndpoints();
        Assert.NotEmpty(endpoints);

        var missing = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var raw = endpoint.RoutePattern.RawText!.TrimStart('/');
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                if (!AnonymousAllowlist.Contains(raw))
                    missing.Add($"anonymous but not allowlisted: {Describe(endpoint)}");
                continue;
            }

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy);
            if (!policies.Contains(AuthorizationPolicies.SessionMember))
                missing.Add(Describe(endpoint));
        }

        Assert.True(missing.Count == 0, "Session-keyed endpoints without the SessionMember policy:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TheAuditedEndpoints_AreAmongThoseChecked()
    {
        var routes = SessionKeyedEndpoints().Select(e => e.RoutePattern.RawText!.TrimStart('/')).ToHashSet();

        foreach (var expected in new[]
        {
            "api/sessions/{sessionId:guid}/rolls",
            "api/sessions/{sessionId:guid}/manual-rolls",
            "api/sessions/{sessionId:guid}/ai/proposals",
            "api/sessions/{sessionId:guid}/combat",
            "api/sessions/{sessionId:guid}/combat/public",
            "api/sessions/{id:guid}",
            "api/sessions/{id:guid}/participants",
            "api/characters/{id:guid}",
            "api/campaigns/{id:guid}",
            "api/campaigns/{campaignId:guid}/state",
        })
        {
            Assert.Contains(expected, routes);
        }
    }

    [Fact]
    public void TheAnonymousAllowlist_HasNoStaleEntries()
    {
        var anonymous = SessionKeyedEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText!.TrimStart('/'))
            .ToHashSet();

        Assert.Subset(anonymous, AnonymousAllowlist);
    }
}
