using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Ai;
using Xunit;

namespace Aircane.UnitTests.Ai;

public class AiAuthorityConfigurationServiceTests
{
    private readonly IAiAuthorityService _sut = new AiAuthorityConfigurationService();

    // ── GetConfiguration ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(AiAuthority.SuggestOnly)]
    [InlineData(AiAuthority.AskBeforeApplying)]
    [InlineData(AiAuthority.AutoApplySafeActions)]
    [InlineData(AiAuthority.FullSessionControl)]
    public void GetConfiguration_ReturnsConfigForAllAuthorities(AiAuthority authority)
    {
        var config = _sut.GetConfiguration(authority);

        Assert.NotNull(config);
        Assert.Equal(authority, config.Authority);
        Assert.False(string.IsNullOrWhiteSpace(config.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(config.Description));
    }

    [Fact]
    public void GetConfiguration_InvalidAuthority_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.GetConfiguration((AiAuthority)999));
    }

    // ── GetAllConfigurations ──────────────────────────────────────────────────

    [Fact]
    public void GetAllConfigurations_ReturnsFourLevels()
    {
        var configs = _sut.GetAllConfigurations();

        Assert.Equal(4, configs.Count);
        Assert.Contains(configs, c => c.Authority == AiAuthority.SuggestOnly);
        Assert.Contains(configs, c => c.Authority == AiAuthority.AskBeforeApplying);
        Assert.Contains(configs, c => c.Authority == AiAuthority.AutoApplySafeActions);
        Assert.Contains(configs, c => c.Authority == AiAuthority.FullSessionControl);
    }

    // ── SuggestOnly ───────────────────────────────────────────────────────────

    [Fact]
    public void SuggestOnly_RequiresApproval_ForAllStateChangingActions()
    {
        var stateChanging = new[]
        {
            AiActionType.Narrate,
            AiActionType.RequestRoll,
            AiActionType.ApplyDamage,
            AiActionType.ApplyHealing,
            AiActionType.ApplyCondition,
            AiActionType.RemoveCondition,
            AiActionType.RevealContent,
            AiActionType.MoveScene,
            AiActionType.CreateNPC,
            AiActionType.StartEncounter,
            AiActionType.AdvanceTurn,
            AiActionType.AwardTreasure,
            AiActionType.AddQuestFlag,
            AiActionType.UpdateWorldFlag,
        };

        foreach (var action in stateChanging)
        {
            Assert.True(_sut.RequiresApproval(AiAuthority.SuggestOnly, action),
                $"SuggestOnly should require approval for {action}");
        }
    }

    [Fact]
    public void SuggestOnly_OnlyAutoApplies_ClarifyingQuestion()
    {
        Assert.True(_sut.CanAutoApply(AiAuthority.SuggestOnly, AiActionType.AskClarifyingQuestion));
        Assert.False(_sut.CanAutoApply(AiAuthority.SuggestOnly, AiActionType.Narrate));
        Assert.False(_sut.CanAutoApply(AiAuthority.SuggestOnly, AiActionType.RequestRoll));
        Assert.False(_sut.CanAutoApply(AiAuthority.SuggestOnly, AiActionType.ApplyDamage));
    }

    // ── AskBeforeApplying ─────────────────────────────────────────────────────

    [Fact]
    public void AskBeforeApplying_AutoApplies_SafeActions()
    {
        Assert.True(_sut.CanAutoApply(AiAuthority.AskBeforeApplying, AiActionType.Narrate));
        Assert.True(_sut.CanAutoApply(AiAuthority.AskBeforeApplying, AiActionType.RequestRoll));
        Assert.True(_sut.CanAutoApply(AiAuthority.AskBeforeApplying, AiActionType.AskClarifyingQuestion));
    }

    [Theory]
    [InlineData(AiActionType.ApplyDamage)]
    [InlineData(AiActionType.ApplyHealing)]
    [InlineData(AiActionType.ApplyCondition)]
    [InlineData(AiActionType.RemoveCondition)]
    [InlineData(AiActionType.RevealContent)]
    [InlineData(AiActionType.MoveScene)]
    [InlineData(AiActionType.CreateNPC)]
    [InlineData(AiActionType.StartEncounter)]
    [InlineData(AiActionType.AdvanceTurn)]
    [InlineData(AiActionType.AwardTreasure)]
    [InlineData(AiActionType.AddQuestFlag)]
    [InlineData(AiActionType.UpdateWorldFlag)]
    public void AskBeforeApplying_RequiresApproval_ForStateChangingActions(AiActionType actionType)
    {
        Assert.True(_sut.RequiresApproval(AiAuthority.AskBeforeApplying, actionType));
        Assert.False(_sut.CanAutoApply(AiAuthority.AskBeforeApplying, actionType));
    }

    // ── AutoApplySafeActions ──────────────────────────────────────────────────

    [Fact]
    public void AutoApplySafeActions_AutoApplies_NonDestructiveActions()
    {
        Assert.True(_sut.CanAutoApply(AiAuthority.AutoApplySafeActions, AiActionType.Narrate));
        Assert.True(_sut.CanAutoApply(AiAuthority.AutoApplySafeActions, AiActionType.RequestRoll));
        Assert.True(_sut.CanAutoApply(AiAuthority.AutoApplySafeActions, AiActionType.AskClarifyingQuestion));
    }

    [Theory]
    [InlineData(AiActionType.ApplyDamage)]
    [InlineData(AiActionType.ApplyHealing)]
    [InlineData(AiActionType.ApplyCondition)]
    [InlineData(AiActionType.RemoveCondition)]
    [InlineData(AiActionType.RevealContent)]
    [InlineData(AiActionType.MoveScene)]
    [InlineData(AiActionType.CreateNPC)]
    [InlineData(AiActionType.StartEncounter)]
    [InlineData(AiActionType.AdvanceTurn)]
    [InlineData(AiActionType.AwardTreasure)]
    [InlineData(AiActionType.AddQuestFlag)]
    [InlineData(AiActionType.UpdateWorldFlag)]
    public void AutoApplySafeActions_RequiresApproval_ForStateChangingActions(AiActionType actionType)
    {
        Assert.True(_sut.RequiresApproval(AiAuthority.AutoApplySafeActions, actionType));
        Assert.False(_sut.CanAutoApply(AiAuthority.AutoApplySafeActions, actionType));
    }

    // ── FullSessionControl ────────────────────────────────────────────────────

    [Fact]
    public void FullSessionControl_AutoApplies_AllActions()
    {
        foreach (var action in Enum.GetValues<AiActionType>())
        {
            Assert.True(_sut.CanAutoApply(AiAuthority.FullSessionControl, action),
                $"FullSessionControl should auto-apply {action}");
        }
    }

    [Fact]
    public void FullSessionControl_NeverRequiresApproval()
    {
        foreach (var action in Enum.GetValues<AiActionType>())
        {
            Assert.False(_sut.RequiresApproval(AiAuthority.FullSessionControl, action),
                $"FullSessionControl should not require approval for {action}");
        }
    }

    // ── Authority hierarchy: progressively more permissive ────────────────────

    [Fact]
    public void AutoApplyActions_AreProgressivelyMorePermissive()
    {
        var suggestOnly = _sut.GetConfiguration(AiAuthority.SuggestOnly);
        var askBefore = _sut.GetConfiguration(AiAuthority.AskBeforeApplying);
        var autoSafe = _sut.GetConfiguration(AiAuthority.AutoApplySafeActions);
        var fullControl = _sut.GetConfiguration(AiAuthority.FullSessionControl);

        Assert.True(suggestOnly.AutoApplyActions.Count <= askBefore.AutoApplyActions.Count);
        Assert.True(askBefore.AutoApplyActions.Count <= autoSafe.AutoApplyActions.Count);
        Assert.True(autoSafe.AutoApplyActions.Count <= fullControl.AutoApplyActions.Count);
    }

    [Fact]
    public void RequiresApprovalActions_AreProgressivelyLessRestrictive()
    {
        var suggestOnly = _sut.GetConfiguration(AiAuthority.SuggestOnly);
        var askBefore = _sut.GetConfiguration(AiAuthority.AskBeforeApplying);
        var autoSafe = _sut.GetConfiguration(AiAuthority.AutoApplySafeActions);
        var fullControl = _sut.GetConfiguration(AiAuthority.FullSessionControl);

        Assert.True(suggestOnly.RequiresApprovalActions.Count >= askBefore.RequiresApprovalActions.Count);
        Assert.True(askBefore.RequiresApprovalActions.Count >= autoSafe.RequiresApprovalActions.Count);
        Assert.True(autoSafe.RequiresApprovalActions.Count >= fullControl.RequiresApprovalActions.Count);
    }

    // ── RequiresApproval / CanAutoApply consistency ───────────────────────────

    [Theory]
    [InlineData(AiAuthority.SuggestOnly)]
    [InlineData(AiAuthority.AskBeforeApplying)]
    [InlineData(AiAuthority.AutoApplySafeActions)]
    [InlineData(AiAuthority.FullSessionControl)]
    public void RequiresApproval_And_CanAutoApply_AreConsistent(AiAuthority authority)
    {
        foreach (var action in Enum.GetValues<AiActionType>())
        {
            var requires = _sut.RequiresApproval(authority, action);
            var canAuto = _sut.CanAutoApply(authority, action);

            // An action cannot both require approval and be auto-applicable.
            Assert.False(requires && canAuto,
                $"Action {action} at {authority} cannot both require approval and be auto-applicable.");
        }
    }

    // ── Unknown authority defaults ────────────────────────────────────────────

    [Fact]
    public void RequiresApproval_UnknownAuthority_ReturnsTrue()
    {
        Assert.True(_sut.RequiresApproval((AiAuthority)999, AiActionType.ApplyDamage));
    }

    [Fact]
    public void CanAutoApply_UnknownAuthority_ReturnsFalse()
    {
        Assert.False(_sut.CanAutoApply((AiAuthority)999, AiActionType.Narrate));
    }

    // ── Safe vs state-changing classification ─────────────────────────────────

    [Fact]
    public void SafeActions_AreCorrectlyClassified()
    {
        var safeActions = AiAuthorityConfigurationService.GetSafeActions();

        Assert.Contains(AiActionType.Narrate, safeActions);
        Assert.Contains(AiActionType.RequestRoll, safeActions);
        Assert.Contains(AiActionType.AskClarifyingQuestion, safeActions);
    }

    [Fact]
    public void StateChangingActions_AreCorrectlyClassified()
    {
        var stateChanging = AiAuthorityConfigurationService.GetStateChangingActions();

        Assert.Contains(AiActionType.ApplyDamage, stateChanging);
        Assert.Contains(AiActionType.ApplyHealing, stateChanging);
        Assert.Contains(AiActionType.ApplyCondition, stateChanging);
        Assert.Contains(AiActionType.RemoveCondition, stateChanging);
        Assert.Contains(AiActionType.RevealContent, stateChanging);
        Assert.Contains(AiActionType.MoveScene, stateChanging);
        Assert.Contains(AiActionType.CreateNPC, stateChanging);
        Assert.Contains(AiActionType.StartEncounter, stateChanging);
        Assert.Contains(AiActionType.AdvanceTurn, stateChanging);
        Assert.Contains(AiActionType.AwardTreasure, stateChanging);
        Assert.Contains(AiActionType.AddQuestFlag, stateChanging);
        Assert.Contains(AiActionType.UpdateWorldFlag, stateChanging);
    }

    [Fact]
    public void SafeActions_DoNotOverlap_WithStateChangingActions()
    {
        var safe = AiAuthorityConfigurationService.GetSafeActions();
        var stateChanging = AiAuthorityConfigurationService.GetStateChangingActions();

        foreach (var action in safe)
        {
            Assert.DoesNotContain(action, stateChanging);
        }
    }
}
