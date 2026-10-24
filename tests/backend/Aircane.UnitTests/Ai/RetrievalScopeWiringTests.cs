using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Aircane.Infrastructure.Retrieval;
using Aircane.Workers.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// The AI DM and the rules-question service hand retrieval the campaign's definition scope, and the AI DM
/// prompt's "Ruleset:" line shows the definition's label instead of the campaign's stored version.
/// </summary>
public sealed class RetrievalScopeWiringTests : IDisposable
{
    private const string DndName = "Dungeons & Dragons 5th Edition (2014)";

    private readonly AircaneDbContext _db;
    private readonly CampaignStateService _stateService;
    private readonly FakeCampaignServiceForPlayerAction _campaigns = new();
    private readonly FakeRagContextBuilder _rag = new();
    private readonly MessageCapturingAiProvider _ai = new();
    private readonly RetrievalScopeResolver _resolver;

    public RetrievalScopeWiringTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
        _stateService = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
        _resolver = new RetrievalScopeResolver(_db, new GameSystemCanonicalizer(_db), new GameSystemDefinitionSerializer());

        new GameSystemDefinitionSeeder(_db, new GameSystemDefinitionSerializer(), new GameSystemDefinitionValidator(),
            NullLogger<GameSystemDefinitionSeeder>.Instance).SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private PlayerActionService CreateSut(IRetrievalScopeResolver? resolver)
    {
        var roleService = new AiRoleConfigurationService();
        var authorityService = new AiAuthorityConfigurationService();
        var executor = new StateCommandExecutor(
            new IStateCommandValidator[] { new RequestRollValidator() },
            roleService, authorityService, _stateService,
            new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance),
            NullLogger<StateCommandExecutor>.Instance);

        return new PlayerActionService(
            _campaigns, _stateService, _rag, _ai,
            new AiOutputParser(new AiStructuredOutputValidator()),
            executor,
            new Aircane.UnitTests.TestDoubles.UnboundSystemRegistry(),
            new AiContextAdapter(),
            _db,
            NullLogger<PlayerActionService>.Instance,
            retrievalScopeResolver: resolver);
    }

    private async Task<PlayerActionRequest> SeedCampaignAsync(string gameSystem, string ruleset, Guid? definitionId = null)
    {
        var campaign = new Campaign("Campaign", gameSystem, ruleset, AiRole.FullDm, AiAuthority.FullSessionControl);
        var session = new Session(campaign.Id, "Session", SessionAccessMode.LocalLan, "hash");
        _db.Campaigns.Add(campaign);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _campaigns.RegisterCampaign(new CampaignDto(
            campaign.Id, campaign.Name, gameSystem, ruleset, campaign.AiRole, campaign.AiAuthority,
            null, campaign.CreatedAt, campaign.UpdatedAt, GameSystemDefinitionId: definitionId));
        return new PlayerActionRequest(session.Id, campaign.Id, Guid.Empty, "I grapple the bandit");
    }

    private string SystemPrompt() => _ai.LastMessages!.First(m => m.Role == "system").Content;

    // ── AI DM ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AiDm_ForASelectorCampaign_PassesTheDefinitionScope_AndPrintsTheLabelNotTheVersion()
    {
        var request = await SeedCampaignAsync(DndName, "1.0.0");

        await CreateSut(_resolver).ProcessActionAsync(request);

        var scope = _rag.LastRequest!.Scope;
        Assert.NotNull(scope);
        Assert.Equal("dnd-5e-2014", scope.DefinitionIdentifier);
        Assert.Contains("Ruleset: 2014", SystemPrompt());
        Assert.DoesNotContain("1.0.0", SystemPrompt());
        Assert.Contains($"Game System: {DndName}", SystemPrompt());
    }

    [Fact]
    public async Task AiDm_ForACampaignBoundOnlyByDefinitionId_UsesThatDefinition()
    {
        var request = await SeedCampaignAsync("Our Tuesday game", "1.0.0", Pathfinder2eRemasterSeed.DefinitionId);

        await CreateSut(_resolver).ProcessActionAsync(request);

        Assert.Equal("pathfinder-2e-remaster", _rag.LastRequest!.Scope?.DefinitionIdentifier);
        Assert.Contains("Ruleset: Remaster", SystemPrompt());
    }

    [Fact]
    public async Task AiDm_ForADefinitionWithoutRulesets_PrintsTheDefinitionName()
    {
        var request = await SeedCampaignAsync("Generic Freeform", "1.0.0");

        await CreateSut(_resolver).ProcessActionAsync(request);

        Assert.Contains("Ruleset: Generic Freeform", SystemPrompt());
    }

    [Fact]
    public async Task AiDm_ForAnUnresolvedCampaign_PassesNoScope_AndPrintsItsOwnRuleset()
    {
        var request = await SeedCampaignAsync("Homebrew Hack", "v2");

        await CreateSut(_resolver).ProcessActionAsync(request);

        Assert.Null(_rag.LastRequest!.Scope);
        Assert.Equal("Homebrew Hack", _rag.LastRequest.GameSystem);
        Assert.Equal("v2", _rag.LastRequest.Ruleset);
        Assert.Contains("Ruleset: v2", SystemPrompt());
    }

    [Fact]
    public async Task AiDm_WhenTheResolverFails_FallsBackToTheCampaignsStrings()
    {
        var request = await SeedCampaignAsync(DndName, "1.0.0");

        await CreateSut(new ThrowingResolver()).ProcessActionAsync(request);

        Assert.Null(_rag.LastRequest!.Scope);
        Assert.Contains("Ruleset: 1.0.0", SystemPrompt());
    }

    [Fact]
    public async Task AiDm_WithoutAResolver_BehavesAsBefore()
    {
        var request = await SeedCampaignAsync(DndName, "1.0.0");

        await CreateSut(resolver: null).ProcessActionAsync(request);

        Assert.Null(_rag.LastRequest!.Scope);
        Assert.Contains("Ruleset: 1.0.0", SystemPrompt());
    }

    [Fact]
    public async Task TheGoldenFixturesCampaign_ResolvesToTheLabelTheFixtureAlreadyPrints()
    {
        // The solo golden fixture's campaign is "D&D 5e" / "2014" and prints "Ruleset: 2014".
        var scope = await _resolver.ResolveAsync(null, "D&D 5e");

        Assert.Equal("2014", scope?.DisplayRuleset);
    }

    // ── Rules question ────────────────────────────────────────────────────────

    private RulesQuestionService RulesService() =>
        new(_rag, new FakeAiProviderForPlayerAction(), _db, NullLogger<RulesQuestionService>.Instance, _resolver);

    [Fact]
    public async Task RulesQuestion_ForACampaignWithNoRulesetPicked_UsesTheCampaignsScope()
    {
        var request = await SeedCampaignAsync(DndName, "1.0.0");

        await RulesService().AskAsync(new RulesQuestionRequest("How does grappling work?", CampaignId: request.CampaignId));

        Assert.Equal("dnd-5e-2014", _rag.LastRequest!.Scope?.DefinitionIdentifier);
    }

    [Fact]
    public async Task RulesQuestion_WithAUserPickedRuleset_IsUnchanged_EvenForACampaign()
    {
        var request = await SeedCampaignAsync(DndName, "1.0.0");

        await RulesService().AskAsync(new RulesQuestionRequest(
            "How does grappling work?", GameSystem: "D&D 5e", Ruleset: "2024", CampaignId: request.CampaignId));

        Assert.Null(_rag.LastRequest!.Scope);
        Assert.Equal("D&D 5e", _rag.LastRequest.GameSystem);
        Assert.Equal("2024", _rag.LastRequest.Ruleset);
    }

    [Fact]
    public async Task RulesQuestion_WithoutACampaign_IsUnchanged()
    {
        await RulesService().AskAsync(new RulesQuestionRequest("How does grappling work?", GameSystem: "D&D 5e"));

        Assert.Null(_rag.LastRequest!.Scope);
        Assert.Equal("D&D 5e", _rag.LastRequest.GameSystem);
        Assert.Null(_rag.LastRequest.Ruleset);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class MessageCapturingAiProvider : IAiProvider
    {
        public string ProviderName => "Capturing";

        public IReadOnlyList<AiMessage>? LastMessages { get; private set; }

        public Task<string> ChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        {
            LastMessages = messages;
            return Task.FromResult("Narration.");
        }

        public Task<AiStructuredOutput> StructuredChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        {
            LastMessages = messages;
            return Task.FromResult(new AiStructuredOutput { Narration = "Narration.", ProposedActions = [] });
        }
    }

    private sealed class ThrowingResolver : IRetrievalScopeResolver
    {
        public Task<RetrievalScope?> ResolveAsync(Guid? gameSystemDefinitionId, string? gameSystem, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Resolver failed.");

        public Task<RetrievalScope?> ResolveForCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Resolver failed.");
    }
}
