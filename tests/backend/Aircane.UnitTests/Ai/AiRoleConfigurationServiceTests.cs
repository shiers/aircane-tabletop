using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Ai;
using Xunit;

namespace Aircane.UnitTests.Ai;

public class AiRoleConfigurationServiceTests
{
    private readonly IAiRoleConfigurationService _sut = new AiRoleConfigurationService();

    // ── GetConfiguration ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(AiRole.Assistant)]
    [InlineData(AiRole.CoDm)]
    [InlineData(AiRole.FullDm)]
    [InlineData(AiRole.Hybrid)]
    public void GetConfiguration_ReturnsConfigForAllRoles(AiRole role)
    {
        var config = _sut.GetConfiguration(role);

        Assert.NotNull(config);
        Assert.Equal(role, config.Role);
        Assert.False(string.IsNullOrWhiteSpace(config.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(config.Description));
        Assert.NotEmpty(config.Capabilities);
    }

    [Fact]
    public void GetConfiguration_InvalidRole_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.GetConfiguration((AiRole)999));
    }

    // ── GetAllConfigurations ──────────────────────────────────────────────────

    [Fact]
    public void GetAllConfigurations_ReturnsFourRoles()
    {
        var configs = _sut.GetAllConfigurations();

        Assert.Equal(4, configs.Count);
        Assert.Contains(configs, c => c.Role == AiRole.Assistant);
        Assert.Contains(configs, c => c.Role == AiRole.CoDm);
        Assert.Contains(configs, c => c.Role == AiRole.FullDm);
        Assert.Contains(configs, c => c.Role == AiRole.Hybrid);
    }

    // ── Assistant capabilities ────────────────────────────────────────────────

    [Fact]
    public void Assistant_HasSuggestionsRulesLookupSummariesDmHelp()
    {
        var caps = _sut.GetCapabilities(AiRole.Assistant);

        Assert.Contains(AiRoleCapability.Suggestions, caps);
        Assert.Contains(AiRoleCapability.RulesLookup, caps);
        Assert.Contains(AiRoleCapability.Summaries, caps);
        Assert.Contains(AiRoleCapability.DmFacingHelp, caps);
    }

    [Theory]
    [InlineData(AiRoleCapability.Narration)]
    [InlineData(AiRoleCapability.NpcDialogue)]
    [InlineData(AiRoleCapability.RequestRolls)]
    [InlineData(AiRoleCapability.RevealContent)]
    [InlineData(AiRoleCapability.RunNpcs)]
    [InlineData(AiRoleCapability.ProposeStateChanges)]
    [InlineData(AiRoleCapability.SceneLevelOverride)]
    public void Assistant_DoesNotHaveAdvancedCapabilities(AiRoleCapability capability)
    {
        Assert.False(_sut.HasCapability(AiRole.Assistant, capability));
    }

    // ── Co-DM capabilities ────────────────────────────────────────────────────

    [Theory]
    [InlineData(AiRoleCapability.Suggestions)]
    [InlineData(AiRoleCapability.RulesLookup)]
    [InlineData(AiRoleCapability.Summaries)]
    [InlineData(AiRoleCapability.DmFacingHelp)]
    [InlineData(AiRoleCapability.RulesReferee)]
    [InlineData(AiRoleCapability.NpcDialogue)]
    [InlineData(AiRoleCapability.Narration)]
    [InlineData(AiRoleCapability.CombatSupport)]
    [InlineData(AiRoleCapability.SessionScribe)]
    public void CoDm_HasDelegatedDuties(AiRoleCapability capability)
    {
        Assert.True(_sut.HasCapability(AiRole.CoDm, capability));
    }

    [Theory]
    [InlineData(AiRoleCapability.RequestRolls)]
    [InlineData(AiRoleCapability.RevealContent)]
    [InlineData(AiRoleCapability.RunNpcs)]
    [InlineData(AiRoleCapability.ProposeStateChanges)]
    [InlineData(AiRoleCapability.SceneLevelOverride)]
    public void CoDm_DoesNotHaveFullDmCapabilities(AiRoleCapability capability)
    {
        Assert.False(_sut.HasCapability(AiRole.CoDm, capability));
    }

    // ── Full DM capabilities ──────────────────────────────────────────────────

    [Theory]
    [InlineData(AiRoleCapability.Suggestions)]
    [InlineData(AiRoleCapability.RulesLookup)]
    [InlineData(AiRoleCapability.Summaries)]
    [InlineData(AiRoleCapability.DmFacingHelp)]
    [InlineData(AiRoleCapability.RulesReferee)]
    [InlineData(AiRoleCapability.NpcDialogue)]
    [InlineData(AiRoleCapability.Narration)]
    [InlineData(AiRoleCapability.CombatSupport)]
    [InlineData(AiRoleCapability.SessionScribe)]
    [InlineData(AiRoleCapability.RequestRolls)]
    [InlineData(AiRoleCapability.RevealContent)]
    [InlineData(AiRoleCapability.RunNpcs)]
    [InlineData(AiRoleCapability.ProposeStateChanges)]
    public void FullDm_HasAllOperationalCapabilities(AiRoleCapability capability)
    {
        Assert.True(_sut.HasCapability(AiRole.FullDm, capability));
    }

    [Fact]
    public void FullDm_DoesNotHaveSceneLevelOverride()
    {
        Assert.False(_sut.HasCapability(AiRole.FullDm, AiRoleCapability.SceneLevelOverride));
    }

    // ── Hybrid capabilities ───────────────────────────────────────────────────

    [Fact]
    public void Hybrid_HasSceneLevelOverride()
    {
        Assert.True(_sut.HasCapability(AiRole.Hybrid, AiRoleCapability.SceneLevelOverride));
    }

    [Fact]
    public void Hybrid_HasAllFullDmCapabilities()
    {
        var fullDmCaps = _sut.GetCapabilities(AiRole.FullDm);
        var hybridCaps = _sut.GetCapabilities(AiRole.Hybrid);

        foreach (var cap in fullDmCaps)
        {
            Assert.Contains(cap, hybridCaps);
        }
    }

    // ── Role hierarchy: each role is a superset of the previous ───────────────

    [Fact]
    public void CoDm_IsSuperset_OfAssistant()
    {
        var assistantCaps = _sut.GetCapabilities(AiRole.Assistant);
        var coDmCaps = _sut.GetCapabilities(AiRole.CoDm);

        foreach (var cap in assistantCaps)
        {
            Assert.Contains(cap, coDmCaps);
        }

        // Co-DM has more capabilities than Assistant
        Assert.True(coDmCaps.Count > assistantCaps.Count);
    }

    [Fact]
    public void FullDm_IsSuperset_OfCoDm()
    {
        var coDmCaps = _sut.GetCapabilities(AiRole.CoDm);
        var fullDmCaps = _sut.GetCapabilities(AiRole.FullDm);

        foreach (var cap in coDmCaps)
        {
            Assert.Contains(cap, fullDmCaps);
        }

        Assert.True(fullDmCaps.Count > coDmCaps.Count);
    }

    // ── IsActionPermitted ─────────────────────────────────────────────────────

    [Fact]
    public void AskClarifyingQuestion_IsAlwaysPermitted()
    {
        Assert.True(_sut.IsActionPermitted(AiRole.Assistant, AiActionType.AskClarifyingQuestion));
        Assert.True(_sut.IsActionPermitted(AiRole.CoDm, AiActionType.AskClarifyingQuestion));
        Assert.True(_sut.IsActionPermitted(AiRole.FullDm, AiActionType.AskClarifyingQuestion));
        Assert.True(_sut.IsActionPermitted(AiRole.Hybrid, AiActionType.AskClarifyingQuestion));
    }

    [Theory]
    [InlineData(AiActionType.ApplyDamage)]
    [InlineData(AiActionType.ApplyHealing)]
    [InlineData(AiActionType.ApplyCondition)]
    [InlineData(AiActionType.RemoveCondition)]
    [InlineData(AiActionType.MoveScene)]
    [InlineData(AiActionType.AwardTreasure)]
    [InlineData(AiActionType.AddQuestFlag)]
    [InlineData(AiActionType.UpdateWorldFlag)]
    public void StateChangingActions_NotPermitted_ForAssistant(AiActionType actionType)
    {
        Assert.False(_sut.IsActionPermitted(AiRole.Assistant, actionType));
    }

    [Theory]
    [InlineData(AiActionType.ApplyDamage)]
    [InlineData(AiActionType.ApplyHealing)]
    [InlineData(AiActionType.RequestRoll)]
    [InlineData(AiActionType.RevealContent)]
    [InlineData(AiActionType.MoveScene)]
    public void FullDm_CanPerformAllActions(AiActionType actionType)
    {
        Assert.True(_sut.IsActionPermitted(AiRole.FullDm, actionType));
    }

    [Fact]
    public void Narrate_PermittedForCoDm_NotForAssistant()
    {
        Assert.True(_sut.IsActionPermitted(AiRole.CoDm, AiActionType.Narrate));
        Assert.False(_sut.IsActionPermitted(AiRole.Assistant, AiActionType.Narrate));
    }

    [Fact]
    public void RequestRoll_PermittedForFullDm_NotForCoDm()
    {
        Assert.True(_sut.IsActionPermitted(AiRole.FullDm, AiActionType.RequestRoll));
        Assert.False(_sut.IsActionPermitted(AiRole.CoDm, AiActionType.RequestRoll));
    }

    // ── GetCapabilities for unknown role ──────────────────────────────────────

    [Fact]
    public void GetCapabilities_UnknownRole_ReturnsEmpty()
    {
        var caps = _sut.GetCapabilities((AiRole)999);

        Assert.Empty(caps);
    }
}
