using System.Security.Claims;
using Aircane.Api.Controllers;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Verifies Rules Lookup retrieves with the caller's own role rather than a hardcoded Host role,
/// so players never receive DM-only chunks.
/// </summary>
public class RulesQuestionControllerRoleTests
{
    private sealed class CapturingRulesService : IRulesQuestionService
    {
        public ParticipantRole? Role { get; private set; }

        public Task<RulesQuestionResponse> AskAsync(
            RulesQuestionRequest request,
            CancellationToken cancellationToken = default,
            ParticipantRole requesterRole = ParticipantRole.Player)
        {
            Role = requesterRole;
            return Task.FromResult(new RulesQuestionResponse("ok", [], false));
        }
    }

    private sealed class NoopRetrieval : IRetrievalService
    {
        public Task<IReadOnlyList<ChunkResultDto>> SearchByKeywordAsync(SearchRequest r, ContentVisibility v, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ChunkResultDto>>([]);
        public Task<IReadOnlyList<ChunkResultDto>> SearchByVectorAsync(SearchRequest r, ContentVisibility v, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ChunkResultDto>>([]);
        public Task<IReadOnlyList<ChunkResultDto>> SearchAsync(SearchRequest r, ContentVisibility v, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ChunkResultDto>>([]);
        public Task<IReadOnlyList<string>> GetAvailableRulesetsAsync(string? g, CancellationToken c = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private static async Task<ParticipantRole?> AskAs(ClaimsPrincipal user)
    {
        var service = new CapturingRulesService();
        var controller = new RulesQuestionController(service, new NoopRetrieval())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };

        await controller.AskRulesQuestion(new RulesQuestionRequest("How does grappling work?"), CancellationToken.None);
        return service.Role;
    }

    private static ClaimsPrincipal Authenticated(string? role) =>
        new(new ClaimsIdentity(
            role is null ? [] : [new Claim(ClaimTypes.Role, role)],
            authenticationType: "test"));

    [Theory]
    [InlineData("Host", ParticipantRole.Host)]
    [InlineData("HumanDm", ParticipantRole.HumanDm)]
    [InlineData("Player", ParticipantRole.Player)]
    [InlineData("Spectator", ParticipantRole.Spectator)]
    public async Task UsesCallersRoleClaim(string claim, ParticipantRole expected)
    {
        Assert.Equal(expected, await AskAs(Authenticated(claim)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bogus")]
    [InlineData("99")]
    public async Task MissingOrUnknownRole_FallsBackToPlayer(string? claim)
    {
        Assert.Equal(ParticipantRole.Player, await AskAs(Authenticated(claim)));
    }

    [Fact]
    public async Task UnauthenticatedLocalHost_IsTreatedAsHost()
    {
        // Only reachable when policies are opened up (Development / TrustLocalHost).
        Assert.Equal(ParticipantRole.Host, await AskAs(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
