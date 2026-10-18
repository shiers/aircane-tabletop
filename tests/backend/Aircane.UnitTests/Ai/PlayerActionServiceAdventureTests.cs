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
    private readonly FakeRagContextBuilder _rag = new();
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
            new IStateCommandValidator[] { new MoveSceneValidator(), new RevealContentValidator(), new RequestRollValidator() },
            roleService, authorityService, _stateService,
            new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance),
            NullLogger<StateCommandExecutor>.Instance);

        return new PlayerActionService(
            _campaignService,
            _stateService,
            _rag,
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
    public async Task PlayerActionService_ActiveAdventure_PromptEnforcesSkillChecks()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I climb the wall"));

        var system = SystemPrompt();
        Assert.Contains("MANDATORY ROLL TRIGGERS", system);
        foreach (var trigger in new[] { "climbing, jumping, swimming", "hiding, sneaking", "persuasion, deception, intimidation",
                     "detecting traps", "identifying magic", "Any combat action" })
            Assert.Contains(trigger, system);
        Assert.Contains("NEVER auto-succeed", system);
        Assert.Contains("NARRATE THEN ROLL", system);
        Assert.Contains("Wait for the player to report the result", system);
        Assert.Contains("Roll Athletics (DC 12)", system);                  // the correct example
        Assert.Contains("Easy task: DC 8", system);
        Assert.Contains("Nearly impossible: DC 24", system);
        Assert.Contains("frost-covered wall in darkness = DC 14", system);
        Assert.Contains("Do not default to the lowest DC", system);
    }

    [Fact]
    public async Task PlayerActionService_NoAdventure_PromptDoesNotCarryTheAdventureSkillCheckRules()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        Assert.DoesNotContain("MANDATORY ROLL TRIGGERS", SystemPrompt());
    }

    // The AI's behaviour is prompt-driven, so what can be tested is that it is told to describe exits.
    [Fact]
    public async Task AdventureSoloSession_DescribesExits_InNewSceneNarration()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I enter the hall"));

        var system = SystemPrompt();
        Assert.Contains("SCENE EXITS", system);
        Assert.Contains("whenever a new scene begins", system);
        Assert.Contains("where they can go", system);
        Assert.Contains("describe EVERY available exit", system);
    }

    [Fact]
    public async Task AdventurePrompt_ExitDescriptionRule_RequiresSensoryDetail()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I look around"));

        var system = SystemPrompt();
        Assert.Contains("at least one sensory detail (sight, sound, smell, or feeling)", system);
        Assert.Contains("whenever a choice between exits is relevant", system);
        Assert.Contains("The player must be able to picture what lies ahead before choosing", system);
        Assert.Contains("Exit descriptions must appear in the narration, not just as names", system);
        // The wrong / correct examples.
        Assert.Contains("WRONG: \"Two paths are available: Scavengers at the Threshold and The Chamber of the Unrisen Sun.\"", system);
        Assert.Contains("CORRECT: \"Two paths lead onward. To your left, a low archway opens onto a chamber where pale blue light filters through cracked stone", system);
        Assert.Contains("the air carries the faint smell of old incense and cold metal", system);
        Assert.Contains("a wider passage echoes with the sound of something scraping rhythmically", system);
        // Edge cases.
        Assert.Contains("If the scene has only one exit, describe it", system);
        Assert.Contains("If the scene has no exits, tell the player what must be done to progress", system);
    }

    [Fact]
    public async Task AdventurePrompt_IncludesDetectionScenario_InMandatoryRollTriggers()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I walk into the guardroom"));

        var system = SystemPrompt();
        var triggers = system[system.IndexOf("MANDATORY ROLL TRIGGERS", StringComparison.Ordinal)..system.IndexOf("NARRATE THEN ROLL", StringComparison.Ordinal)];
        // The detection trigger is one of the mandatory triggers, not floating elsewhere in the prompt.
        Assert.Contains("Any awareness or detection scenario", triggers);
        Assert.Contains("entering a space where NPCs are present", triggers);
        Assert.Contains("checking whether you have been noticed", triggers);
        Assert.Contains("attempting to observe someone without being seen", triggers);
        Assert.Contains("hostile or wary creatures could detect you", triggers);
        Assert.Contains("These require a Dexterity (Stealth) check", triggers);
        Assert.Contains("distracted NPCs DC 8, alert NPCs DC 14, supernaturally aware entities DC 18", triggers);
        Assert.Contains("NEVER narrate whether the player is noticed or unnoticed without a roll", triggers);
        Assert.Contains("Ask for the Stealth check first", triggers);
        // The combat trigger that follows it is intact.
        Assert.Contains("Any combat action", triggers);
    }

    [Fact]
    public async Task AdventurePrompt_RequiresEndOfNarrationPlayerPrompt()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I look around"));

        var system = SystemPrompt();
        Assert.Contains("END OF NARRATION", system);
        Assert.Contains("always close your narration with a clear signal that it is the player's turn to act", system);
        // The three accepted endings, with their examples.
        Assert.Contains("A direct question", system);
        Assert.Contains("\"What do you do?\"", system);
        Assert.Contains("A presented choice", system);
        Assert.Contains("Do you press forward or hold your ground?", system);
        Assert.Contains("A clear tension statement", system);
        Assert.Contains("The moment hangs in the air.", system);
        Assert.Contains("Never end narration with description alone", system);
        Assert.Contains("The player must always know it is their turn", system);
    }

    [Fact]
    public async Task AdventurePrompt_EndOfNarrationRule_IsNotAddedOutsideAnAdventure()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        Assert.DoesNotContain("END OF NARRATION", SystemPrompt());
    }

    [Fact]
    public async Task PlayerActionService_NoAdventure_PromptDoesNotCarryTheExitRules()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        Assert.DoesNotContain("SCENE EXITS", SystemPrompt());
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
    public async Task PlayerActionService_NoCharacter_PromptTellsTheAiNotToUseStatsAndToWaitForTheResult()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I sneak past"));

        var system = string.Join("\n", _aiProvider.LastMessages.Where(m => m.Role == "system").Select(m => m.Content));
        Assert.Contains("NO CHARACTER ATTACHED", system);
        Assert.Contains("Roll Dexterity (Stealth)", system);
        Assert.Contains("Do NOT apply modifiers", system);
        Assert.Contains("wait for the player's next message", system);
    }

    [Fact]
    public async Task PlayerActionService_WithCharacter_PromptHasNoNoCharacterSection()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(
            new PlayerActionRequest(session.Id, campaign.Id, Guid.NewGuid(), "I sneak past"));

        var system = string.Join("\n", _aiProvider.LastMessages.Where(m => m.Role == "system").Select(m => m.Content));
        Assert.DoesNotContain("NO CHARACTER ATTACHED", system);
    }

    [Fact]
    public async Task PlayerActionService_RequestRollWithoutCharacter_IsAppliedNotRejected()
    {
        var (campaign, session) = await SeedCampaignAsync();
        _aiProvider.NextOutput = new AiStructuredOutput
        {
            Narration = "The guard turns. Roll Dexterity (Stealth) against DC 14.",
            ProposedActions = [new AiProposedAction { Type = AiActionType.RequestRoll, Label = "Dexterity (Stealth)", Formula = "1d20", Dc = 14 }],
        };

        var response = await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I sneak past"));

        Assert.Equal("The guard turns. Roll Dexterity (Stealth) against DC 14.", response.Narration);
        var roll = Assert.Single(response.ProposedActions);
        Assert.Equal(StateCommandOutcome.Applied, roll.Outcome);
        Assert.Null(roll.ErrorMessage);
    }

    // ── Solo vs group instruction blocks ──────────────────────────────────────

    // The golden file was captured from the solo prompt before group runs got their own instruction
    // block, so any change to what a solo run sends the AI fails here. GUIDs are masked (they are new
    // each run) and line endings normalised (AppendLine uses the platform's); nothing else is touched.
    [Fact]
    public async Task SoloAdventurePrompt_IsByteForByteUnchanged()
    {
        var (campaign, session, _) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = AsksForStealthRoll();
        await sut.ProcessActionAsync(Request(campaign, session, "I sneak past the guard"));
        _aiProvider.NextOutput = Output();

        await sut.ProcessActionAsync(Request(campaign, session, "I rolled a 14"));

        var golden = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Ai", "Golden", "solo-adventure-prompt.golden.txt"));
        Assert.Equal(golden, RenderPrompt(_aiProvider.LastMessages));
    }

    [Fact]
    public async Task GroupAdventurePrompt_SendsTheGroupBlock_AndNoSoloText()
    {
        var (campaign, session) = await SeedGroupRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I check the shelves"));

        var system = SystemPrompt();
        Assert.Contains("GROUP ADVENTURE MODE:", system);
        Assert.Contains("TURN ROTATION", system);
        Assert.Contains("OUT-OF-TURN INPUT", system);
        Assert.Contains("PARTY-WIDE EVENTS", system);
        Assert.Contains("GROUP PLAY — 2 players are in this session.", system);
        Assert.DoesNotContain("SOLO ADVENTURE MODE", system);
        Assert.DoesNotContain("single player", system);
        foreach (var line in SoloOnlyLines())
            Assert.DoesNotContain(line, system);
    }

    [Fact]
    public async Task SoloAdventurePrompt_SendsTheSoloBlock_AndNoGroupText()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I check the shelves"));

        var system = SystemPrompt();
        Assert.Contains("SOLO ADVENTURE MODE:", system);
        Assert.DoesNotContain("GROUP ADVENTURE MODE", system);
        Assert.DoesNotContain("GROUP PLAY", system);
        Assert.DoesNotContain("TURN ROTATION", system);
        Assert.DoesNotContain("OUT-OF-TURN INPUT", system);
        Assert.DoesNotContain("PARTY-WIDE EVENTS", system);
        foreach (var line in GroupOnlyLines())
            Assert.DoesNotContain(line, system);
    }

    // The same selection at the prompt-assembly level, without a run behind it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildPromptMessages_SendsExactlyOneAdventureBlock_ChosenByRunType(bool isGroupRun)
    {
        var (request, campaign, state, rag) = PromptInputs();

        var messages = PlayerActionService.BuildPromptMessages(
            request, campaign, state, rag, combatContext: null, adventureContext: "Current Adventure: Test", isGroupRun: isGroupRun);

        var system = messages.Where(m => m.Role == "system").Select(m => m.Content).ToList();
        var expected = isGroupRun ? PlayerActionService.BuildGroupAdventureInstructions() : PlayerActionService.BuildAdventureInstructions();
        var other = isGroupRun ? PlayerActionService.BuildAdventureInstructions() : PlayerActionService.BuildGroupAdventureInstructions();
        Assert.Single(system, m => m == expected);
        Assert.DoesNotContain(other, system);
        var all = string.Join("\n", system);
        foreach (var line in isGroupRun ? SoloOnlyLines() : GroupOnlyLines())
            Assert.DoesNotContain(line, all);
    }

    [Fact]
    public void BuildPromptMessages_NoAdventure_IgnoresTheGroupFlag()
    {
        var (request, campaign, state, rag) = PromptInputs();

        var messages = PlayerActionService.BuildPromptMessages(
            request, campaign, state, rag, isGroupRun: true);

        var system = string.Join("\n", messages.Select(m => m.Content));
        Assert.DoesNotContain("GROUP ADVENTURE MODE", system);
        Assert.DoesNotContain("SOLO ADVENTURE MODE", system);
    }

    [Fact]
    public void GroupAdventureInstructions_CoverRotationAddressingOutOfTurnAndPartyEvents()
    {
        var block = PlayerActionService.BuildGroupAdventureInstructions();

        // Turn rotation: the host passes the turn, the AI keeps the spotlight on the acting character.
        Assert.Contains("The host passes the turn to the next player; you never do.", block);
        Assert.Contains("never choose their actions, words or decisions for them", block);
        // Addressing the acting player in character.
        Assert.Contains("Address the acting player by their character's name", block);
        Assert.Contains("handing the moment back to the acting character, by their character's name", block);
        // Out-of-turn input.
        Assert.Contains("Resolve only the acting character's action.", block);
        Assert.Contains("Never let an out-of-turn action replace or cancel the acting character's turn.", block);
        // Party-wide events: one shared roll request, consistent with the single pending roll per run.
        Assert.Contains("emit ONE RequestRoll whose label names the check and every character who rolls", block);
        Assert.Contains("never invent one", block);
        // Consistent with the combat context block's heading and terms.
        Assert.Contains("ACTIVE COMBAT ENCOUNTER", block);
        Assert.Contains("Active turn", block);
        // The shared rules ride along unchanged.
        Assert.Contains(PlayerActionService.BuildSkillCheckRules(), block);
        Assert.Contains(PlayerActionService.BuildExitRules(), block);
        Assert.DoesNotContain(PlayerActionService.BuildEndOfNarrationRule(), block);
    }

    private static (PlayerActionRequest, CampaignDto, Application.DTOs.CampaignState.CampaignStateDto, Application.DTOs.Retrieval.RagContextResult) PromptInputs()
    {
        var campaign = new CampaignDto(
            Id: Guid.NewGuid(), Name: "C", GameSystem: "D&D 5e", Ruleset: "2014", AiRole: AiRole.FullDm,
            AiAuthority: AiAuthority.FullSessionControl, ActiveAdventureId: null,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        var state = new Application.DTOs.CampaignState.CampaignStateDto(
            CampaignId: campaign.Id, ActiveSessionId: Guid.NewGuid(), CurrentSceneJson: null, StateJson: null,
            SnapshotAt: DateTimeOffset.UtcNow);
        var rag = new Application.DTOs.Retrieval.RagContextResult(
            ContextText: "", Citations: [], TotalChunksRetrieved: 0, ChunksIncluded: 0);
        var request = new PlayerActionRequest(Guid.NewGuid(), campaign.Id, Guid.Empty, "I wait");
        return (request, campaign, state, rag);
    }

    /// <summary>Lines of the solo block that the group block does not share (the shared rules are excluded).</summary>
    private static IEnumerable<string> SoloOnlyLines() => UniqueLines(
        PlayerActionService.BuildAdventureInstructions(), PlayerActionService.BuildGroupAdventureInstructions());

    /// <summary>Lines of the group block that the solo block does not share.</summary>
    private static IEnumerable<string> GroupOnlyLines() => UniqueLines(
        PlayerActionService.BuildGroupAdventureInstructions(), PlayerActionService.BuildAdventureInstructions());

    private static IEnumerable<string> UniqueLines(string block, string otherBlock)
    {
        var other = Lines(otherBlock).ToHashSet();
        var unique = Lines(block).Where(l => !other.Contains(l)).ToList();
        Assert.NotEmpty(unique);
        return unique;
    }

    private static IEnumerable<string> Lines(string text) => text
        .ReplaceLineEndings("\n")
        .Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0);

    /// <summary>A group run of two approved players, Alice up first; Alice plays Mira.</summary>
    private async Task<(Campaign Campaign, Session Session)> SeedGroupRunAsync()
    {
        var (campaign, session) = await SeedCampaignAsync();
        var mira = new Character("Mira", "D&D 5e", "2014", 3, "{}", "{}", campaign.Id);
        var alice = new SessionParticipant(session.Id, "Alice", ParticipantRole.Player, isApproved: true, characterId: mira.Id);
        var bob = new SessionParticipant(session.Id, "Bob", ParticipantRole.Player, isApproved: true);
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.Characters.Add(mira);
        _db.SessionParticipants.AddRange(alice, bob);
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        await _realAdventureService.StartGroupSessionAsync(adventure.Id, campaign.Id, [alice.Id, bob.Id]);
        return (campaign, session);
    }

    /// <summary>Every message the AI was sent, in order, with GUIDs masked and line endings normalised.</summary>
    private static string RenderPrompt(IReadOnlyList<AiMessage> messages)
    {
        var text = string.Join("\n", messages.Select(m => $"===== {m.Role} =====\n{m.Content}"));
        return System.Text.RegularExpressions.Regex.Replace(
            text.ReplaceLineEndings("\n"),
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "<guid>");
    }

    // ── Retrieval during an adventure ─────────────────────────────────────────

    private async Task<(Campaign Campaign, Session Session, Guid RunId)> SeedRunAsync()
    {
        var (campaign, session) = await SeedCampaignAsync();
        var adventure = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(adventure);
        await _db.SaveChangesAsync();
        var run = await _realAdventureService.StartAdventureSessionAsync(adventure.Id, campaign.Id, null);
        return (campaign, session, run.Id);
    }

    [Fact]
    public async Task PlayerActionService_ActiveAdventure_RetrievesWithoutRerankingAtStandardSettings()
    {
        var (campaign, session, _) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        var rag = _rag.LastRequest!;
        Assert.True(rag.SkipReranking);
        Assert.Equal(10, rag.TopK);
        Assert.Equal(10, rag.RerankCandidateTopK);
        Assert.Equal(0.4, rag.MinRelevanceScore);
        Assert.Null(rag.PostRerankTopN);
    }

    [Fact]
    public async Task PlayerActionService_NoAdventure_StillAsksForReranking()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        var rag = _rag.LastRequest!;
        Assert.False(rag.SkipReranking);
        Assert.Equal(25, rag.RerankCandidateTopK);
        Assert.Equal(7, rag.PostRerankTopN);
    }

    // ── RequestRoll label ─────────────────────────────────────────────────────

    [Fact]
    public async Task PlayerActionService_RequestRoll_WithoutLabel_IsRejected()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RequestRoll, Formula = "1d20", Dc = 14 });

        var response = await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        var result = Assert.Single(response.ProposedActions);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Label", result.ErrorMessage);
        Assert.Null((await Run(runId)).PendingRollLabel);   // nothing to remember without a label
    }

    [Fact]
    public async Task PlayerActionService_RequestRoll_WithLabel_IsAccepted()
    {
        var (campaign, session, _) = await SeedRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RequestRoll, Label = "Dexterity (Stealth) check", Formula = "1d20" });

        var response = await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        Assert.Equal(StateCommandOutcome.Applied, Assert.Single(response.ProposedActions).Outcome);
    }

    [Fact]
    public async Task PlayerActionService_SystemPrompt_SaysRequestRollNeedsALabel()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        var system = string.Join("\n", _aiProvider.LastMessages.Where(m => m.Role == "system").Select(m => m.Content));
        Assert.Contains("RequestRoll MUST include a 'label' field", system);
        Assert.Contains("Dexterity (Stealth) check", system);
        Assert.Contains("will be rejected", system);
    }

    // ── Pending roll between turns ────────────────────────────────────────────

    private Task<AdventureSession> Run(Guid runId) => _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == runId);

    private string SystemPrompt() =>
        string.Join("\n", _aiProvider.LastMessages.Where(m => m.Role == "system").Select(m => m.Content));

    private static AiStructuredOutput AsksForStealthRoll() => Output(new AiProposedAction
    {
        Type = AiActionType.RequestRoll, Label = "Dexterity (Stealth)", Formula = "1d20", Dc = 14,
    });

    [Fact]
    public async Task AdventureSession_StoresPendingRoll_WhenAiRequestsRoll()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        _aiProvider.NextOutput = AsksForStealthRoll();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I sneak past"));

        var run = await Run(runId);
        Assert.Equal("Dexterity (Stealth)", run.PendingRollLabel);
        Assert.Equal(14, run.PendingRollDC);
    }

    [Fact]
    public async Task AdventureSession_PendingRollWithoutDc_OmitsTheDcFromTheContext()
    {
        var (campaign, session, _) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RequestRoll, Label = "Perception", Formula = "1d20" });
        await sut.ProcessActionAsync(Request(campaign, session));

        _aiProvider.NextOutput = Output();
        await sut.ProcessActionAsync(Request(campaign, session, "I rolled a 9"));

        var block = PendingBlock(SystemPrompt());
        Assert.Contains("asked to roll Perception.", block);
        Assert.DoesNotContain("DC", block);
    }

    private static string PendingBlock(string prompt)
    {
        var start = prompt.IndexOf("PENDING ROLL", StringComparison.Ordinal);
        return start < 0 ? "" : prompt[start..prompt.IndexOf("Current Adventure", start, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task AdventureSession_IncludesPendingRoll_InNextTurnContext()
    {
        var (campaign, session, _) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = AsksForStealthRoll();
        await sut.ProcessActionAsync(Request(campaign, session, "I sneak past"));
        Assert.DoesNotContain("PENDING ROLL", SystemPrompt());   // not in the turn that asked for it

        _aiProvider.NextOutput = Output();
        await sut.ProcessActionAsync(Request(campaign, session, "I rolled a 14"));

        var prompt = SystemPrompt();
        Assert.Contains("PENDING ROLL: The player was previously asked to roll Dexterity (Stealth), DC 14.", prompt);
        Assert.True(prompt.IndexOf("PENDING ROLL", StringComparison.Ordinal) < prompt.IndexOf("Current Adventure", StringComparison.Ordinal),
            "the pending roll should come first in the adventure context");
    }

    [Fact]
    public async Task AdventureSession_AssumesPendingRoll_WhenPlayerReportsNumber()
    {
        var (campaign, session, _) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = AsksForStealthRoll();
        await sut.ProcessActionAsync(Request(campaign, session, "I sneak past"));

        _aiProvider.NextOutput = Output();
        await sut.ProcessActionAsync(Request(campaign, session, "I rolled a 14"));

        var prompt = SystemPrompt();
        Assert.Contains("do NOT ask them to clarify what the roll was for", prompt);
        Assert.Contains("Assume it is this pending roll", prompt);
        Assert.Contains("Narrate the outcome of the roll before continuing the scene", prompt);
        Assert.Equal("Player action: I rolled a 14", _aiProvider.LastMessages.Last(m => m.Role == "user").Content);
    }

    [Fact]
    public async Task AdventureSession_ClearsPendingRoll_AfterNextAction()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = AsksForStealthRoll();
        await sut.ProcessActionAsync(Request(campaign, session, "I sneak past"));

        _aiProvider.NextOutput = Output();
        await sut.ProcessActionAsync(Request(campaign, session, "I rolled a 14"));

        var run = await Run(runId);
        Assert.Null(run.PendingRollLabel);
        Assert.Null(run.PendingRollDC);

        await sut.ProcessActionAsync(Request(campaign, session, "I look around"));
        Assert.DoesNotContain("PENDING ROLL", SystemPrompt());   // and it does not come back
    }

    [Fact]
    public async Task AdventureSession_ClearsPendingRoll_EvenIfPlayerSaysUnrelated()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = AsksForStealthRoll();
        await sut.ProcessActionAsync(Request(campaign, session, "I sneak past"));

        _aiProvider.NextOutput = Output();
        await sut.ProcessActionAsync(Request(campaign, session, "Actually, what time is it?"));

        Assert.Null((await Run(runId)).PendingRollLabel);
        await sut.ProcessActionAsync(Request(campaign, session, "I open the door"));
        Assert.DoesNotContain("PENDING ROLL", SystemPrompt());
    }

    [Fact]
    public async Task AdventureSession_ANewRollRequest_ReplacesThePendingOne()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = AsksForStealthRoll();
        await sut.ProcessActionAsync(Request(campaign, session, "I sneak past"));

        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RequestRoll, Label = "Athletics", Formula = "1d20", Dc = 12 });
        await sut.ProcessActionAsync(Request(campaign, session, "I rolled a 14"));

        var run = await Run(runId);
        Assert.Equal("Athletics", run.PendingRollLabel);
        Assert.Equal(12, run.PendingRollDC);
    }

    [Fact]
    public async Task AdventureSession_NoAdventureRunning_NeverTouchesPendingRolls()
    {
        var (campaign, session) = await SeedCampaignAsync();
        _aiProvider.NextOutput = AsksForStealthRoll();

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(_spy.PendingRollUpdates);
        Assert.Equal(StateCommandOutcome.Applied, Assert.Single(response.ProposedActions).Outcome);
    }

    [Fact]
    public async Task AdventureSession_SpyReceivesTheRollThatWasAsked()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _aiProvider.NextOutput = AsksForStealthRoll();

        await CreateSut(_spy).ProcessActionAsync(Request(campaign, session));

        var update = Assert.Single(_spy.PendingRollUpdates);
        Assert.Equal((_spy.RunId, "Dexterity (Stealth)", 14), (update.RunId, update.Label, update.Dc));
    }

    // ── Narration history ─────────────────────────────────────────────────────

    private Task<List<AdventureSessionEntry>> Entries(Guid runId) =>
        _db.AdventureSessionEntries.AsNoTracking().Where(e => e.AdventureSessionId == runId)
            .OrderBy(e => e.CreatedAt).ToListAsync();

    [Fact]
    public async Task AdventureSessionEntry_SavedAfterPlayerAction()
    {
        var (campaign, session, runId) = await SeedRunAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I search the shelves"));

        var player = Assert.Single(await Entries(runId), e => e.EntryType == AdventureEntryType.PlayerAction);
        Assert.Equal("I search the shelves", player.Content);
        Assert.Equal("scene-1", player.SceneId);
    }

    [Fact]
    public async Task AdventureSessionEntry_SavedAfterAiNarration()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-2" });

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session, "I go to the hall"));

        var entries = await Entries(runId);
        Assert.Equal([AdventureEntryType.PlayerAction, AdventureEntryType.AiNarration], entries.Select(e => e.EntryType));
        Assert.Equal("The story continues.", entries[1].Content);
        // Each entry is stamped with the scene it belongs to: the action was taken in scene-1, the
        // narration is written once the move to scene-2 has been applied.
        Assert.Equal("scene-1", entries[0].SceneId);
        Assert.Equal("scene-2", entries[1].SceneId);
    }

    [Fact]
    public async Task AdventureSessionEntry_AccumulatesAcrossTurns_InOrder()
    {
        var (campaign, session, runId) = await SeedRunAsync();
        var sut = CreateSut(_realAdventureService);
        _aiProvider.NextOutput = Output();

        await sut.ProcessActionAsync(Request(campaign, session, "first"));
        await sut.ProcessActionAsync(Request(campaign, session, "second"));

        Assert.Equal(["first", "The story continues.", "second", "The story continues."], (await Entries(runId)).Select(e => e.Content));
    }

    [Fact]
    public async Task AdventureSessionEntry_FailureDoesNotBlockAction()
    {
        var (campaign, session) = await SeedWithSpyRunAsync();
        _spy.ThrowOnRecord = true;
        _aiProvider.NextOutput = Output(new AiProposedAction { Type = AiActionType.RevealClue, ClueId = "clue-1" });

        var response = await CreateSut(_spy).ProcessActionAsync(Request(campaign, session, "I search"));

        Assert.Equal("The story continues.", response.Narration);
        Assert.Equal(StateCommandOutcome.Applied, Assert.Single(response.ProposedActions).Outcome);   // the action itself still ran
        Assert.Empty(_spy.Exchanges);
    }

    [Fact]
    public async Task AdventureSessionEntry_NotSaved_WhenNoAdventureIsRunning()
    {
        var (campaign, session) = await SeedCampaignAsync();

        await CreateSut(_realAdventureService).ProcessActionAsync(Request(campaign, session));

        Assert.Empty(await _db.AdventureSessionEntries.ToListAsync());
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

        public List<(Guid RunId, string? Label, int? Dc)> PendingRollUpdates { get; } = [];
        public Task ResumeAsync(Guid sessionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GenerateRecapAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public List<(Guid RunId, string Player, string Narration, string? Scene)> Exchanges { get; } = [];
        public bool ThrowOnRecord { get; set; }

        public Task RecordExchangeAsync(Guid sessionId, string playerAction, string narration, string? sceneIdAtStart, CancellationToken ct = default)
        {
            if (ThrowOnRecord) throw new InvalidOperationException("transcript store is down");
            Exchanges.Add((sessionId, playerAction, narration, sceneIdAtStart));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AdventureHistoryEntryDto>> GetHistoryAsync(Guid sessionId, int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventureSession>> ListInProgressAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task UpdatePendingRollAsync(Guid sessionId, string? label, int? dc, CancellationToken ct = default)
        {
            PendingRollUpdates.Add((sessionId, label, dc));
            return Task.CompletedTask;
        }

        public Task CompleteAdventureAsync(Guid sessionId, string outcomeNotes, CancellationToken ct = default)
        {
            Calls.Add(("CompleteAdventure", sessionId, outcomeNotes, null));
            return Task.CompletedTask;
        }

        public Task<AdventureSession> StartAdventureSessionAsync(Guid generatedAdventureId, Guid campaignId, Guid? characterId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession> StartGroupSessionAsync(Guid generatedAdventureId, Guid campaignId, IReadOnlyList<Guid> participantIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession> AdvanceTurnAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession> AddParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AttachPlaySessionAsync(Guid sessionId, Guid playSessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSession?> GetActiveByCampaignAsync(Guid campaignId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdventureSessionStateDto> GetSessionStateAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventureExitDto>> GetAvailableScenesAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AbandonAdventureAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public string BuildAdventureContext(AdventureSession session, GeneratedAdventureDto adventure) => throw new NotSupportedException();
    }
}
