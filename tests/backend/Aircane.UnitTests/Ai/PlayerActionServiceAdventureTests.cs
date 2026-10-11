using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Aircane.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Tests for the solo-adventure branch of <see cref="PlayerActionService"/>: adventure context in
/// the prompt, and AI-proposed adventure actions being applied through
/// <see cref="IAdventureSessionService"/>.
/// </summary>
public sealed class PlayerActionServiceAdventureTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly FakeCampaignServiceForPlayerAction _campaignService = new();
    private readonly CapturingAiProvider _aiProvider = new();
    private readonly CampaignStateService _stateService;
    private readonly AdventureSessionService _realAdventureService;
    private readonly SpyAdventureSessionService _spy = new();

    public PlayerActionServiceAdventureTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _stateService = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
        _realAdventureService = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            _stateService,
            NullLogger<AdventureSessionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private PlayerActionService CreateSut(IAdventureSessionService adventureService, bool withPolicyServices = true)
    {
        var roleService = new AiRoleConfigurationService();
        var authorityService = new AiAuthorityConfigurationService();
        var executor = new StateCommandExecutor(
            new IStateCommandValidator[] { new MoveSceneValidator(), new RevealContentValidator() },
            roleService, authorityService, _stateService,
            new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance),
            NullLogger<StateCommandExecutor>.Instance);

        return new PlayerActionService(
            _campaignService,
            _stateService,
            new FakeRagContextBuilder(),
            _aiProvider,
            new AiOutputParser(new AiStructuredOutputValidator()),
            executor,
            new UnboundSystemRegistry(),
            new Aircane.Application.GameSystems.AiContextAdapter(),
            _db,
            NullLogger<PlayerActionService>.Instance,
            adventureService,
            withPolicyServices ? roleService : null,
            withPolicyServices ? authorityService : null);
    }

    private async Task<(Campaign Campaign, Session Session)> SeedCampaignAsync(
        AiRole role = AiRole.FullDm,
        AiAuthority authority = AiAuthority.FullSessionControl)
    {
        var campaign = new Campaign("Solo Campaign", "D&D 5e", "2014", role, authority);
        var session = new Session(campaign.Id, "Solo", SessionAccessMode.Solo, "hash");
        _db.Campaigns.Add(campaign);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _campaignService.RegisterCampaign(new CampaignDto(
            campaign.Id, campaign.Name, campaign.GameSystem, campaign.Ruleset,
            campaign.AiRole, campaign.AiAuthority, null, campaign.CreatedAt, campaign.UpdatedAt));
        return (campaign, session);
    }

    private static PlayerActionRequest Request(Campaign campaign, Session session, string text = "I search the shelves")
        => new(session.Id, campaign.Id, Guid.Empty, text);

    /// <summary>A spy session service that reports an in-progress run, so the AI loop has one to act on.</summary>
    private async Task<(Campaign, Session)> SeedWithSpyRunAsync(
        AiRole role = AiRole.FullDm, AiAuthority authority = AiAuthority.FullSessionControl)
    {
        var seeded = await SeedCampaignAsync(role, authority);
        _spy.ActiveContext = new AdventureContext(_spy.RunId, "Current Adventure: Spy Adventure");
        return seeded;
    }

    // ── Delegation to IAdventureSessionService ────────────────────────────────

    [Fact]
    public async Task PlayerActionService_AdventureAction_AdvanceScene_CallsSessionService()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction
        {
            Type = AiActionType.AdvanceScene, SceneId = "scene-2", Narration = "You enter the hall.",
        });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        var call = Assert.Single(_spy.Calls);
        Assert.Equal(("AdvanceScene", _spy.RunId, "scene-2", "You enter the hall."), (call.Method, call.RunId, call.Arg1, call.Arg2));
        var result = Assert.Single(response.ProposedActions);
        Assert.Equal(AiActionType.AdvanceScene, result.ActionType);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task PlayerActionService_AdventureAction_RevealClue_CallsSessionService()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RevealClue, ClueId = "clue-1" });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        var call = Assert.Single(_spy.Calls);
        Assert.Equal(("RevealClue", _spy.RunId, "clue-1"), (call.Method, call.RunId, call.Arg1));
        Assert.Equal(StateCommandOutcome.Applied, Assert.Single(response.ProposedActions).Outcome);
    }

    [Fact]
    public async Task PlayerActionService_AdventureAction_RevealSecretUpdateNpcAndComplete_CallSessionService()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _aiProvider.NextOutput = Output(
            new AiProposedAction { Type = AiActionType.RevealSecret, SecretId = "secret-1" },
            new AiProposedAction
            {
                Type = AiActionType.UpdateNpc, NpcId = "npc-archivist", HasBeenMet = true,
                RevealedInfoId = "hint", Notes = "Helpful",
            },
            new AiProposedAction { Type = AiActionType.CompleteAdventure, OutcomeNotes = "Victory." });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        Assert.Equal(["RevealSecret", "UpdateNpc", "CompleteAdventure"], _spy.Calls.Select(c => c.Method));
        Assert.Equal("secret-1", _spy.Calls[0].Arg1);
        Assert.Equal("npc-archivist", _spy.Calls[1].Arg1);
        Assert.Equal("Victory.", _spy.Calls[2].Arg1);
        Assert.All(response.ProposedActions, r => Assert.Equal(StateCommandOutcome.Applied, r.Outcome));
    }

    // ── Guard rails ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PlayerActionService_AdventureAction_NoActiveRun_IsRejectedWithoutCallingService()
    {
        var (campaign, session) = await SeedCampaignAsync();   // spy.ActiveContext stays null
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-2" });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(_spy.Calls);
        var result = Assert.Single(response.ProposedActions);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("No adventure is in progress", result.ErrorMessage);
    }

    [Theory]
    [InlineData(AiAuthority.SuggestOnly)]
    [InlineData(AiAuthority.AskBeforeApplying)]
    [InlineData(AiAuthority.AutoApplySafeActions)]
    public async Task PlayerActionService_AdventureAction_WithoutFullAuthority_IsDeniedNotQueued(AiAuthority authority)
    {
        var (campaign, session) = await SeedWithSpyRunAsync(authority: authority);
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RevealClue, ClueId = "clue-1" });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(_spy.Calls);
        var result = Assert.Single(response.ProposedActions);
        Assert.Equal(StateCommandOutcome.PermissionDenied, result.Outcome);
        Assert.Contains("Full Session Control", result.ErrorMessage);
        Assert.Equal(0, await _db.AiActionProposals.CountAsync());   // never queued: the queue cannot replay it
    }

    [Fact]
    public async Task PlayerActionService_AdventureAction_RoleWithoutCapability_IsDenied()
    {
        var (campaign, session) = await SeedWithSpyRunAsync(role: AiRole.Assistant);
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-2" });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(_spy.Calls);
        Assert.Equal(StateCommandOutcome.PermissionDenied, Assert.Single(response.ProposedActions).Outcome);
    }

    [Fact]
    public async Task PlayerActionService_AdventureAction_WithoutPolicyServices_FailsClosed()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RevealClue, ClueId = "clue-1" });

        var response = await CreateSut(_spy, withPolicyServices: false).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(_spy.Calls);
        Assert.Equal(StateCommandOutcome.PermissionDenied, Assert.Single(response.ProposedActions).Outcome);
    }

    [Fact]
    public async Task PlayerActionService_AdventureAction_MissingRequiredField_IsRejected()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.AdvanceScene });   // no sceneId

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(_spy.Calls);
        Assert.Equal(StateCommandOutcome.ValidationFailed, Assert.Single(response.ProposedActions).Outcome);
    }

    // ── End to end with the real service ──────────────────────────────────────

    [Fact]
    public async Task PlayerActionService_AdventureAction_InvalidSceneFromAi_ReportsFailureAndKeepsNarrationAndOtherActions()
    {
        var (campaign, session) = await SeedCampaignAsync();
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        var run = await _realAdventureService.StartAdventureSessionAsync(adventure.Id, campaign.Id, null);

        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "You find a letter and dash into a nonexistent room.",
            ProposedActions =
            [
                new AiProposedAction { Type = AiActionType.RevealClue, ClueId = "clue-1" },
                new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-99" },
            ],
        };

        var response = await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        Assert.Equal("You find a letter and dash into a nonexistent room.", response.Narration);
        Assert.Equal(StateCommandOutcome.Applied, response.ProposedActions[0].Outcome);
        Assert.Equal(StateCommandOutcome.ValidationFailed, response.ProposedActions[1].Outcome);
        Assert.Contains("scene-99", response.ProposedActions[1].ErrorMessage);

        var saved = await _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);
        Assert.Equal("scene-1", saved.CurrentSceneId);
        Assert.Equal(["clue-1"], saved.RevealedClueIds);
    }

    [Fact]
    public async Task PlayerActionService_AdventureAction_FullPlaythrough_AdvancesAndCompletes()
    {
        var (campaign, session) = await SeedCampaignAsync();
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        var run = await _realAdventureService.StartAdventureSessionAsync(adventure.Id, campaign.Id, null);
        var sut = CreateSut(_realAdventureService);

        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-2", Narration = "On." });
        await sut.ProcessActionAsync(Request(campaign, session, "I go to the hall"));
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-3" });
        await sut.ProcessActionAsync(Request(campaign, session, "I go through the passage"));
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.CompleteAdventure, OutcomeNotes = "You escaped." });
        await sut.ProcessActionAsync(Request(campaign, session, "I leave"));

        var saved = await _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);
        Assert.Equal(AdventureSessionStatus.Completed, saved.Status);
        Assert.Equal("You escaped.", saved.OutcomeNotes);
        Assert.Equal(["scene-1", "scene-2", "scene-3"], saved.CompletedSceneIds);
    }

    // ── Prompt content ────────────────────────────────────────────────────────

    [Fact]
    public async Task PlayerActionService_ActiveAdventure_PromptIncludesInstructionsAndContext()
    {
        var (campaign, session) = await SeedCampaignAsync();
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        await _realAdventureService.StartAdventureSessionAsync(adventure.Id, campaign.Id, null);

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I look around"));

        var system = string.Join("\n---\n", _aiProvider.LastMessages.Where(m => m.Role == "system").Select(m => m.Content));
        Assert.Contains("SOLO ADVENTURE MODE", system);
        Assert.Contains("\"type\":\"advanceScene\"", system);
        Assert.Contains("ACTIVE ADVENTURE:", system);
        Assert.Contains("Current Scene: The Sunken Library [scene-1]", system);

        // A solo run may have no character: the user message must not carry an empty-GUID tag.
        var user = _aiProvider.LastMessages.Last(m => m.Role == "user").Content;
        Assert.Equal("Player action: I look around", user);
    }

    [Fact]
    public async Task PlayerActionService_NoActiveAdventure_PromptHasNoAdventureSection()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        var system = string.Join("\n", _aiProvider.LastMessages.Where(m => m.Role == "system").Select(m => m.Content));
        Assert.DoesNotContain("SOLO ADVENTURE MODE", system);
        Assert.DoesNotContain("ACTIVE ADVENTURE:", system);
    }

    [Fact]
    public async Task PlayerActionService_WithCharacter_StillTagsTheUserMessage()
    {
        var (campaign, session) = await SeedCampaignAsync();
        var characterId = Guid.NewGuid();

        await CreateSut(_realAdventureService).ProcessActionAsync(
            new PlayerActionRequest(session.Id, campaign.Id, characterId, "I wave"));

        Assert.Equal($"[Character {characterId}] Player action: I wave", _aiProvider.LastMessages.Last(m => m.Role == "user").Content);
    }

    // ── Helpers / doubles ─────────────────────────────────────────────────────

    private static AiStructuredOutput Output(params AiProposedAction[] actions) => new()
    {
        Narration = "The story continues.",
        ProposedActions = actions,
    };

    private sealed class CapturingAiProvider : IAiProvider
    {
        public string ProviderName => "Capturing";
        public AiStructuredOutput NextOutput { get; set; } = new() { Narration = "Default.", ProposedActions = [] };
        public IReadOnlyList<AiMessage> LastMessages { get; private set; } = [];

        public Task<string> ChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
            => Task.FromResult(NextOutput.Narration);

        public Task<AiStructuredOutput> StructuredChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        {
            LastMessages = messages;
            return Task.FromResult(NextOutput);
        }
    }

    /// <summary>Records adventure calls and reports an in-progress run when <see cref="ActiveContext"/> is set.</summary>
    private sealed class SpyAdventureSessionService : IAdventureSessionService
    {
        public Guid RunId { get; } = Guid.NewGuid();
        public AdventureContext? ActiveContext { get; set; }
        public List<(string Method, Guid RunId, string? Arg1, string? Arg2)> Calls { get; } = [];

        public Task<AdventureContext?> BuildActiveAdventureContextAsync(Guid campaignId, CancellationToken ct = default)
            => Task.FromResult(ActiveContext);

        public Task<AdventureSession> AdvanceSceneAsync(Guid sessionId, string targetSceneId, string? transitionNarration, CancellationToken ct = default)
        {
            Calls.Add(("AdvanceScene", sessionId, targetSceneId, transitionNarration));
            return Task.FromResult(new AdventureSession());
        }

        public Task RevealClueAsync(Guid sessionId, string clueId, CancellationToken ct = default)
        {
            Calls.Add(("RevealClue", sessionId, clueId, null));
            return Task.CompletedTask;
        }

        public Task RevealSecretAsync(Guid sessionId, string secretId, CancellationToken ct = default)
        {
            Calls.Add(("RevealSecret", sessionId, secretId, null));
            return Task.CompletedTask;
        }

        public Task UpdateNpcStateAsync(Guid sessionId, string npcId, bool? hasBeenMet, bool? isAlive, string? revealedInfoId, string? playerNotes, CancellationToken ct = default)
        {
            Calls.Add(("UpdateNpc", sessionId, npcId, playerNotes));
            return Task.CompletedTask;
        }

        public Task CompleteAdventureAsync(Guid sessionId, string outcomeNotes, CancellationToken ct = default)
        {
            Calls.Add(("CompleteAdventure", sessionId, outcomeNotes, null));
            return Task.CompletedTask;
        }

        public Task<AdventureSession> StartAdventureSessionAsync(Guid generatedAdventureId, Guid campaignId, Guid? characterId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AttachPlaySessionAsync(Guid sessionId, Guid playSessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession?> GetActiveByCampaignAsync(Guid campaignId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSessionStateDto> GetSessionStateAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventureExitDto>> GetAvailableScenesAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AbandonAdventureAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public string BuildAdventureContext(AdventureSession session, GeneratedAdventureDto adventure) => throw new NotSupportedException();
    }
}
