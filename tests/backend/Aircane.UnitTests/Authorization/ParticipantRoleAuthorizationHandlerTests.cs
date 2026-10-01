using System.Security.Claims;
using Aircane.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Authorization;

public sealed class ParticipantRoleAuthorizationHandlerTests
{
    private readonly ParticipantRoleAuthorizationHandler _handler;

    public ParticipantRoleAuthorizationHandlerTests()
    {
        var logger = NullLogger<ParticipantRoleAuthorizationHandler>.Instance;
        _handler = new ParticipantRoleAuthorizationHandler(logger);
    }

    [Fact]
    public async Task Host_Succeeds_For_HostOnly_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host");
        var user = CreatePrincipal("Host");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HumanDm_Fails_For_HostOnly_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host");
        var user = CreatePrincipal("HumanDm");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Player_Fails_For_HostOnly_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host");
        var user = CreatePrincipal("Player");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Host_Succeeds_For_DmOrHost_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host", "HumanDm");
        var user = CreatePrincipal("Host");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HumanDm_Succeeds_For_DmOrHost_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host", "HumanDm");
        var user = CreatePrincipal("HumanDm");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Player_Fails_For_DmOrHost_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host", "HumanDm");
        var user = CreatePrincipal("Player");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Spectator_Fails_For_DmOrHost_Policy()
    {
        var requirement = new ParticipantRoleRequirement("Host", "HumanDm");
        var user = CreatePrincipal("Spectator");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Unauthenticated_User_Fails()
    {
        var requirement = new ParticipantRoleRequirement("Host");
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Missing_Role_Claim_Fails()
    {
        var requirement = new ParticipantRoleRequirement("Host");
        // Authenticated but no role claim
        var identity = new ClaimsIdentity(
            new[] { new Claim("sub", Guid.NewGuid().ToString()) },
            "Bearer");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [InlineData("host", "Host")]
    [InlineData("HOST", "Host")]
    [InlineData("HumanDm", "HumanDm")]
    [InlineData("humandm", "HumanDm")]
    public async Task Role_Comparison_Is_Case_Insensitive(string tokenRole, string policyRole)
    {
        var requirement = new ParticipantRoleRequirement(policyRole);
        var user = CreatePrincipal(tokenRole);
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData("Player")]
    [InlineData("Host")]
    [InlineData("HumanDm")]
    [InlineData("Spectator")]
    public async Task Any_Authenticated_Role_Passes_When_No_Specific_Role_Required(string role)
    {
        // Simulating the "Authenticated" policy - it uses RequireAuthenticatedUser()
        // without a role requirement. But if we test with a broad requirement:
        var requirement = new ParticipantRoleRequirement("Player", "Host", "HumanDm", "Spectator");
        var user = CreatePrincipal(role);
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClaimsPrincipal CreatePrincipal(string role)
    {
        var claims = new[]
        {
            new Claim("sid", Guid.NewGuid().ToString()),
            new Claim("pid", Guid.NewGuid().ToString()),
            new Claim("dname", "TestUser"),
            new Claim(ClaimTypes.Role, role),
        };

        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizationHandlerContext CreateContext(
        ClaimsPrincipal user,
        IAuthorizationRequirement requirement)
    {
        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            resource: null);
    }
}
