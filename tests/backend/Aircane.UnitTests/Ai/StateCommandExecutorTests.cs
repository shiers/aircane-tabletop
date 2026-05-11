using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.DTOs.Ai;
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
/// Unit tests for <see cref="StateCommandExecutor"/> covering validation,
/// role permission checks, authority routing, and state application.
/// </summary>
public class StateCommandExecutorTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly CampaignStateService _stateService;
    private readonly AiProposalService _proposalService;
    private readonly IAiRoleConfigurationService _roleService;
    private readonly IAiAuthorityService _authorityService;
    private readonly StateCommandExecutor _sut;

    public StateCommandExecutorTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _stateService = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
        _proposalService = new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance);
        _roleService = new AiRoleConfigurationService();
        _authorityService = new AiAuthorityConfigurationService();

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

        _sut = new StateCommandExecutor(
            validators,
            _roleService,
            _authorityService,
            _stateService,
            _proposalService,
            NullLogger<StateCommandExecutor>.Instance);
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
        return (campaign, session);
    }

    private static StateCommandContext CreateContext(
        Campaign campaign,
        Session session) => new(
        CampaignId: campaign.Id,
        SessionId: session.Id,
        AiRole: campaign.AiRole,
        AiAuthority: campaign.AiAuthority);

    // ── Validation Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RequestRoll_MissingCharacterId_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            Label = "Stealth check",
            Formula = "1d20+5",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("CharacterId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_RequestRoll_MissingFormula_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Label = "Stealth check",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Formula", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyDamage_MissingCharacterId_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            Amount = 10,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("CharacterId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyDamage_ZeroAmount_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = 0,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("positive Amount", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyHealing_NegativeAmount_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyHealing,
            CharacterId = Guid.NewGuid(),
            Amount = -5,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("positive Amount", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyCondition_MissingConditionName_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyCondition,
            CharacterId = Guid.NewGuid(),
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("ConditionName", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_RemoveCondition_MissingConditionName_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RemoveCondition,
            CharacterId = Guid.NewGuid(),
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("ConditionName", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_RevealContent_MissingContentId_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RevealContent,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("ContentId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MoveScene_MissingTargetSceneId_ReturnsValidationFailed()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync();
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("TargetSceneId", result.ErrorMessage);
    }

    // ── Permission Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AssistantRole_StateChangingAction_ReturnsPermissionDenied()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.Assistant,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = 10,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.PermissionDenied, result.Outcome);
        Assert.Contains("Assistant", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_AssistantRole_RevealContent_ReturnsPermissionDenied()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.Assistant,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RevealContent,
            ContentId = Guid.NewGuid(),
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.PermissionDenied, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_FullDmRole_AllActionsPermitted()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
            TargetSceneId = Guid.NewGuid(),
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
    }

    // ── Authority Routing Tests ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FullSessionControl_AppliesDirectly()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var sceneId = Guid.NewGuid();
        var command = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
            TargetSceneId = sceneId,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
        Assert.Null(result.ProposalId);

        // Verify state was updated
        var state = await _stateService.LoadStateAsync(campaign.Id);
        Assert.Equal(sceneId.ToString(), state.CurrentSceneJson);
    }

    [Fact]
    public async Task ExecuteAsync_SuggestOnly_QueuesForApproval()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.SuggestOnly);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
            TargetSceneId = Guid.NewGuid(),
            Label = "Move to dungeon",
            Reason = "Players entered the cave",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.QueuedForApproval, result.Outcome);
        Assert.NotNull(result.ProposalId);

        // Verify proposal was created
        var proposals = await _proposalService.GetPendingProposalsAsync(session.Id);
        Assert.Single(proposals);
        Assert.Equal("MoveScene", proposals[0].ActionType);
    }

    [Fact]
    public async Task ExecuteAsync_AskBeforeApplying_SafeAction_AppliesDirectly()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.AskBeforeApplying);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Label = "Perception check",
            Formula = "1d20+3",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_AskBeforeApplying_StateChangingAction_QueuesForApproval()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.AskBeforeApplying);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = 15,
            Reason = "Fireball hit",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.QueuedForApproval, result.Outcome);
        Assert.NotNull(result.ProposalId);
    }

    [Fact]
    public async Task ExecuteAsync_AutoApplySafeActions_SafeAction_AppliesDirectly()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.AutoApplySafeActions);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Label = "Athletics check",
            Formula = "1d20+4",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_AutoApplySafeActions_DamageAction_QueuesForApproval()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.AutoApplySafeActions);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = 8,
            Reason = "Sword slash",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.QueuedForApproval, result.Outcome);
    }

    // ── State Application Tests ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RevealContent_AppliesContentIdToState()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var contentId = Guid.NewGuid();
        var command = new AiProposedAction
        {
            Type = AiActionType.RevealContent,
            ContentId = contentId,
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ValidApplyHealing_AppliesDirectly()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyHealing,
            CharacterId = Guid.NewGuid(),
            Amount = 12,
            Reason = "Cure Wounds",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ValidApplyCondition_AppliesDirectly()
    {
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.FullDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.ApplyCondition,
            CharacterId = Guid.NewGuid(),
            ConditionName = "Poisoned",
            Reason = "Failed CON save",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }

    // ── CoDm Role Tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CoDmRole_RequestRoll_PermissionDenied()
    {
        // CoDm does not have RequestRolls capability
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.CoDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Label = "Stealth check",
            Formula = "1d20+5",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.False(result.Success);
        Assert.Equal(StateCommandOutcome.PermissionDenied, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_CoDmRole_Narrate_Permitted()
    {
        // CoDm has Narration capability
        var (campaign, session) = await SeedCampaignAndSessionAsync(
            aiRole: AiRole.CoDm,
            aiAuthority: AiAuthority.FullSessionControl);
        var context = CreateContext(campaign, session);

        var command = new AiProposedAction
        {
            Type = AiActionType.Narrate,
            Label = "Scene description",
        };

        var result = await _sut.ExecuteAsync(command, context);

        Assert.True(result.Success);
        Assert.Equal(StateCommandOutcome.Applied, result.Outcome);
    }
}
