using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="PlayerActionService"/> covering the full AI DM loop:
/// state loading, RAG context building, AI prompting, output parsing, and command execution.
/// </summary>
public class PlayerActionServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly CampaignStateService _stateService;
    private readonly AiProposalService _proposalService;
    private readonly StateCommandExecutor _commandExecutor;
    private readonly FakeCampaignServiceForPlayerAction _campaignService;
    private readonly FakeRagContextBuilder _ragContextBuilder;
    private readonly FakeAiProviderForPlayerAction _aiProvider;
    private readonly PlayerActionService _sut;

    public PlayerActionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _stateService = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
        _proposalService = new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance);

        var roleService = new AiRoleConfigurationService();
        var authorityService = new AiAuthorityConfigurationService();
        var validators = new IStateCommandValidator[]
        {
            new RequestRollValidator(),
            new ApplyDamageValidator(),
            new ApplyHealingValidator(),
            new ApplyConditionValidator(),
            new RemoveConditionValidator(),
            new RevealContentValidator(),
            new MoveSceneValidator(),
        };

        _commandExecutor = new StateCommandExecutor(
            validators, roleService, authorityService, _stateService, _proposalService,
            NullLogger<StateCommandExecutor>.Instance);

        _campaignService = new FakeCampaignServiceForPlayerAction();
        _ragContextBuilder = new FakeRagContextBuilder();
        _aiProvider = new FakeAiProviderForPlayerAction();

        var outputParser = new AiOutputParser(new AiStructuredOutputValidator());

        _sut = new PlayerActionService(
            _campaignService,
            _stateService,
            _ragContextBuilder,
            _aiProvider,
            outputParser,
            _commandExecutor,
            NullLogger<PlayerActionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(Campaign Campaign, Session Session)> SeedCampaignAndSessionAsync(
        AiRole aiRole = AiRole.FullDm,
        AiAuthority aiAuthority = AiAuthority.FullSessionControl)
    {
        var campaign = new Campaign("Test Campaign", "D&D 5e", "2014", aiRole, aiAuthority);
        _db.Campaigns.Add(campaign);

        var session = new Session(
            campaignId: campaign.Id,
            name: "Test Session",
            accessMode: SessionAccessMode.LocalLan,
            inviteCodeHash: "test-hash");
        _db.Sessions.Add(session);

        await _db.SaveChangesAsync();

        // Register the campaign in the fake service
        _campaignService.RegisterCampaign(new CampaignDto(
            Id: campaign.Id,
            Name: campaign.Name,
            GameSystem: campaign.GameSystem,
            Ruleset: campaign.Ruleset,
            AiRole: campaign.AiRole,
            AiAuthority: campaign.AiAuthority,
            ActiveAdventureId: null,
            CreatedAt: campaign.CreatedAt,
            UpdatedAt: campaign.UpdatedAt));

        return (campaign, session);
    }

    // ── Happy Path Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessActionAsync_ReturnsNarration_WhenAiRespondsWithNoActions()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "You step carefully through the dark corridor.",
            PrivateDmNote = null,
            RulesCitations = [],
            ProposedActions = [],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: Guid.NewGuid(),
            ActionText: "I walk down the corridor");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Equal("You step carefully through the dark corridor.", response.Narration);
        Assert.Null(response.PrivateDmNote);
        Assert.Empty(response.ProposedActions);
    }

    [Fact]
    public async Task ProcessActionAsync_ReturnsPrivateDmNote_WhenAiIncludesOne()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "The guard doesn't notice you.",
            PrivateDmNote = "Player rolled well; skip the encounter.",
            RulesCitations = [],
            ProposedActions = [],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: Guid.NewGuid(),
            ActionText: "I sneak past the guards");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Equal("The guard doesn't notice you.", response.Narration);
        Assert.Equal("Player rolled well; skip the encounter.", response.PrivateDmNote);
    }

    [Fact]
    public async Task ProcessActionAsync_ExecutesProposedActions_WhenFullSessionControl()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);

        var characterId = Guid.NewGuid();
        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "The guard spots you! Roll for initiative.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    CharacterId = characterId,
                    Label = "Initiative",
                    Formula = "1d20+2",
                }
            ],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: characterId,
            ActionText: "I try to sneak past the guards");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Single(response.ProposedActions);
        Assert.Equal(AiActionType.RequestRoll, response.ProposedActions[0].ActionType);
        Assert.Equal(StateCommandOutcome.Applied, response.ProposedActions[0].Outcome);
        Assert.Equal("Initiative", response.ProposedActions[0].Label);
    }

    [Fact]
    public async Task ProcessActionAsync_QueuesActions_WhenSuggestOnly()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.SuggestOnly);

        var characterId = Guid.NewGuid();
        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "The fireball explodes!",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyDamage,
                    CharacterId = characterId,
                    Amount = 28,
                    Label = "Fireball damage",
                    Reason = "Failed DEX save",
                }
            ],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: characterId,
            ActionText: "I cast fireball at the goblins");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Single(response.ProposedActions);
        Assert.Equal(StateCommandOutcome.QueuedForApproval, response.ProposedActions[0].Outcome);
        Assert.NotNull(response.ProposedActions[0].ProposalId);
    }

    [Fact]
    public async Task ProcessActionAsync_HandlesMultipleActions_MixedOutcomes()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.AutoApplySafeActions);

        var characterId = Guid.NewGuid();
        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "You attack the goblin and land a hit!",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    CharacterId = characterId,
                    Label = "Attack roll",
                    Formula = "1d20+5",
                },
                new AiProposedAction
                {
                    Type = AiActionType.ApplyDamage,
                    CharacterId = characterId,
                    Amount = 8,
                    Label = "Sword damage",
                    Reason = "Longsword hit",
                }
            ],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: characterId,
            ActionText: "I attack the goblin with my sword");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Equal(2, response.ProposedActions.Count);
        // RequestRoll is safe → applied
        Assert.Equal(StateCommandOutcome.Applied, response.ProposedActions[0].Outcome);
        // ApplyDamage is not safe → queued
        Assert.Equal(StateCommandOutcome.QueuedForApproval, response.ProposedActions[1].Outcome);
    }

    // ── Validation Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessActionAsync_ReportsValidationFailure_WhenActionInvalid()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);

        // AI proposes a RequestRoll without a formula (invalid)
        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "Roll for stealth!",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    CharacterId = Guid.NewGuid(),
                    Label = "Stealth check",
                    // Missing Formula - should fail validation
                }
            ],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: Guid.NewGuid(),
            ActionText: "I try to hide");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Single(response.ProposedActions);
        Assert.Equal(StateCommandOutcome.ValidationFailed, response.ProposedActions[0].Outcome);
        Assert.NotNull(response.ProposedActions[0].ErrorMessage);
    }

    [Fact]
    public async Task ProcessActionAsync_ThrowsKeyNotFound_WhenCampaignMissing()
    {
        var request = new PlayerActionRequest(
            SessionId: Guid.NewGuid(),
            CampaignId: Guid.NewGuid(), // Not registered
            CharacterId: Guid.NewGuid(),
            ActionText: "I do something");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.ProcessActionAsync(request));
    }

    [Fact]
    public async Task ProcessActionAsync_ThrowsArgumentException_WhenActionTextEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.ProcessActionAsync(new PlayerActionRequest(
                SessionId: Guid.NewGuid(),
                CampaignId: Guid.NewGuid(),
                CharacterId: Guid.NewGuid(),
                ActionText: "")));
    }

    // ── RAG Context Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessActionAsync_IncludesCitations_WhenRagReturnsChunks()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();

        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        _ragContextBuilder.NextResult = new RagContextResult(
            ContextText: "Stealth rules: ...",
            Citations:
            [
                new RagCitation(chunkId, docId, "Player's Handbook", 177, "Stealth")
            ],
            TotalChunksRetrieved: 5,
            ChunksIncluded: 1);

        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "You attempt to hide in the shadows.",
            ProposedActions = [],
        };

        var request = new PlayerActionRequest(
            SessionId: session.Id,
            CampaignId: campaign.Id,
            CharacterId: Guid.NewGuid(),
            ActionText: "I hide behind the barrel");

        var response = await _sut.ProcessActionAsync(request);

        Assert.Single(response.Citations);
        Assert.Equal("Player's Handbook", response.Citations[0].SourceTitle);
        Assert.Equal(177, response.Citations[0].PageNumber);
        Assert.Equal("Stealth", response.Citations[0].SectionTitle);
        Assert.Equal(chunkId, response.Citations[0].ChunkId);
    }

    // ── Prompt Building Tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildPromptMessages_IncludesSystemPrompt_StateContext_RagContext_And_UserAction()
    {
        var campaign = new CampaignDto(
            Id: Guid.NewGuid(),
            Name: "Test",
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            AiRole: AiRole.FullDm,
            AiAuthority: AiAuthority.FullSessionControl,
            ActiveAdventureId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var state = new CampaignStateDto(
            CampaignId: campaign.Id,
            ActiveSessionId: Guid.NewGuid(),
            CurrentSceneJson: "tavern-scene-id",
            StateJson: "{\"hp\": 25}",
            SnapshotAt: DateTimeOffset.UtcNow);

        var ragResult = new RagContextResult(
            ContextText: "[1] Stealth rules...",
            Citations: [new RagCitation(Guid.NewGuid(), Guid.NewGuid(), "PHB", 177, "Stealth")],
            TotalChunksRetrieved: 3,
            ChunksIncluded: 1);

        var request = new PlayerActionRequest(
            SessionId: Guid.NewGuid(),
            CampaignId: campaign.Id,
            CharacterId: Guid.NewGuid(),
            ActionText: "I sneak past the guards");

        var messages = PlayerActionService.BuildPromptMessages(request, campaign, state, ragResult);

        // Should have: system prompt, state context, RAG context, user action
        Assert.Equal(4, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("AI Dungeon Master", messages[0].Content);
        Assert.Contains("D&D 5e", messages[0].Content);
        Assert.Equal("system", messages[1].Role);
        Assert.Contains("tavern-scene-id", messages[1].Content);
        Assert.Equal("system", messages[2].Role);
        Assert.Contains("Stealth rules", messages[2].Content);
        Assert.Equal("user", messages[3].Role);
        Assert.Contains("I sneak past the guards", messages[3].Content);
    }

    [Fact]
    public void BuildPromptMessages_OmitsRagContext_WhenNoChunksIncluded()
    {
        var campaign = new CampaignDto(
            Id: Guid.NewGuid(),
            Name: "Test",
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            AiRole: AiRole.FullDm,
            AiAuthority: AiAuthority.FullSessionControl,
            ActiveAdventureId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var state = new CampaignStateDto(
            CampaignId: campaign.Id,
            ActiveSessionId: null,
            CurrentSceneJson: null,
            StateJson: "{}",
            SnapshotAt: DateTimeOffset.UtcNow);

        var ragResult = new RagContextResult(
            ContextText: "",
            Citations: [],
            TotalChunksRetrieved: 0,
            ChunksIncluded: 0);

        var request = new PlayerActionRequest(
            SessionId: Guid.NewGuid(),
            CampaignId: campaign.Id,
            CharacterId: Guid.NewGuid(),
            ActionText: "I look around");

        var messages = PlayerActionService.BuildPromptMessages(request, campaign, state, ragResult);

        // Should have: system prompt, state context, user action (no RAG context)
        Assert.Equal(3, messages.Count);
        Assert.Equal("user", messages[2].Role);
    }
}

// ── Test Doubles ──────────────────────────────────────────────────────────────

/// <summary>
/// Fake campaign service that returns pre-registered campaigns.
/// </summary>
internal sealed class FakeCampaignServiceForPlayerAction : ICampaignService
{
    private readonly Dictionary<Guid, CampaignDto> _campaigns = new();

    public void RegisterCampaign(CampaignDto campaign) => _campaigns[campaign.Id] = campaign;

    public Task<CampaignDto?> GetCampaignAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_campaigns.GetValueOrDefault(id));

    public Task<CampaignDto> CreateCampaignAsync(CreateCampaignRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<CampaignDto>> ListCampaignsAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<CampaignDto> UpdateCampaignAsync(Guid id, UpdateCampaignRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task DeleteCampaignAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
}

/// <summary>
/// Fake RAG context builder that returns a configurable result.
/// </summary>
internal sealed class FakeRagContextBuilder : IRagContextBuilder
{
    public RagContextResult NextResult { get; set; } = new(
        ContextText: "",
        Citations: [],
        TotalChunksRetrieved: 0,
        ChunksIncluded: 0);

    public Task<RagContextResult> BuildContextAsync(RagContextRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(NextResult);
}

/// <summary>
/// Fake AI provider that returns a configurable structured output.
/// </summary>
internal sealed class FakeAiProviderForPlayerAction : IAiProvider
{
    public string ProviderName => "FakePlayerAction";

    public AiStructuredOutput NextOutput { get; set; } = new()
    {
        Narration = "Default narration.",
        ProposedActions = [],
    };

    public Task<string> ChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        => Task.FromResult(NextOutput.Narration);

    public Task<AiStructuredOutput> StructuredChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        => Task.FromResult(NextOutput);
}
