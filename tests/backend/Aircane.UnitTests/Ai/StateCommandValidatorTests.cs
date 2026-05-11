using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime.Validators;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for individual state command validators.
/// </summary>
public class StateCommandValidatorTests
{
    // ── RequestRollValidator ──────────────────────────────────────────────────

    [Fact]
    public void RequestRoll_ValidAction_ReturnsNull()
    {
        var validator = new RequestRollValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Label = "Stealth check",
            Formula = "1d20+5",
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void RequestRoll_MissingCharacterId_ReturnsError()
    {
        var validator = new RequestRollValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            Label = "Stealth check",
            Formula = "1d20+5",
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("CharacterId", error);
    }

    [Fact]
    public void RequestRoll_EmptyGuidCharacterId_ReturnsError()
    {
        var validator = new RequestRollValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.Empty,
            Label = "Stealth check",
            Formula = "1d20+5",
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("CharacterId", error);
    }

    [Fact]
    public void RequestRoll_MissingLabel_ReturnsError()
    {
        var validator = new RequestRollValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Formula = "1d20+5",
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("Label", error);
    }

    [Fact]
    public void RequestRoll_MissingFormula_ReturnsError()
    {
        var validator = new RequestRollValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RequestRoll,
            CharacterId = Guid.NewGuid(),
            Label = "Stealth check",
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("Formula", error);
    }

    // ── ApplyDamageValidator ──────────────────────────────────────────────────

    [Fact]
    public void ApplyDamage_ValidAction_ReturnsNull()
    {
        var validator = new ApplyDamageValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = 10,
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void ApplyDamage_MissingCharacterId_ReturnsError()
    {
        var validator = new ApplyDamageValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            Amount = 10,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("CharacterId", error);
    }

    [Fact]
    public void ApplyDamage_ZeroAmount_ReturnsError()
    {
        var validator = new ApplyDamageValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = 0,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("positive Amount", error);
    }

    [Fact]
    public void ApplyDamage_NegativeAmount_ReturnsError()
    {
        var validator = new ApplyDamageValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
            Amount = -5,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("positive Amount", error);
    }

    [Fact]
    public void ApplyDamage_NullAmount_ReturnsError()
    {
        var validator = new ApplyDamageValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(),
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("positive Amount", error);
    }

    // ── ApplyHealingValidator ─────────────────────────────────────────────────

    [Fact]
    public void ApplyHealing_ValidAction_ReturnsNull()
    {
        var validator = new ApplyHealingValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyHealing,
            CharacterId = Guid.NewGuid(),
            Amount = 8,
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void ApplyHealing_NegativeAmount_ReturnsError()
    {
        var validator = new ApplyHealingValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyHealing,
            CharacterId = Guid.NewGuid(),
            Amount = -3,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("positive Amount", error);
    }

    // ── ApplyConditionValidator ───────────────────────────────────────────────

    [Fact]
    public void ApplyCondition_ValidAction_ReturnsNull()
    {
        var validator = new ApplyConditionValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyCondition,
            CharacterId = Guid.NewGuid(),
            ConditionName = "Poisoned",
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void ApplyCondition_MissingConditionName_ReturnsError()
    {
        var validator = new ApplyConditionValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyCondition,
            CharacterId = Guid.NewGuid(),
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("ConditionName", error);
    }

    [Fact]
    public void ApplyCondition_MissingCharacterId_ReturnsError()
    {
        var validator = new ApplyConditionValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.ApplyCondition,
            ConditionName = "Prone",
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("CharacterId", error);
    }

    // ── RemoveConditionValidator ──────────────────────────────────────────────

    [Fact]
    public void RemoveCondition_ValidAction_ReturnsNull()
    {
        var validator = new RemoveConditionValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RemoveCondition,
            CharacterId = Guid.NewGuid(),
            ConditionName = "Poisoned",
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void RemoveCondition_MissingConditionName_ReturnsError()
    {
        var validator = new RemoveConditionValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RemoveCondition,
            CharacterId = Guid.NewGuid(),
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("ConditionName", error);
    }

    // ── RevealContentValidator ────────────────────────────────────────────────

    [Fact]
    public void RevealContent_ValidAction_ReturnsNull()
    {
        var validator = new RevealContentValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RevealContent,
            ContentId = Guid.NewGuid(),
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void RevealContent_MissingContentId_ReturnsError()
    {
        var validator = new RevealContentValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RevealContent,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("ContentId", error);
    }

    [Fact]
    public void RevealContent_EmptyGuidContentId_ReturnsError()
    {
        var validator = new RevealContentValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.RevealContent,
            ContentId = Guid.Empty,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("ContentId", error);
    }

    // ── MoveSceneValidator ────────────────────────────────────────────────────

    [Fact]
    public void MoveScene_ValidAction_ReturnsNull()
    {
        var validator = new MoveSceneValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
            TargetSceneId = Guid.NewGuid(),
        };

        Assert.Null(validator.Validate(action));
    }

    [Fact]
    public void MoveScene_MissingTargetSceneId_ReturnsError()
    {
        var validator = new MoveSceneValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("TargetSceneId", error);
    }

    [Fact]
    public void MoveScene_EmptyGuidTargetSceneId_ReturnsError()
    {
        var validator = new MoveSceneValidator();
        var action = new AiProposedAction
        {
            Type = AiActionType.MoveScene,
            TargetSceneId = Guid.Empty,
        };

        var error = validator.Validate(action);
        Assert.NotNull(error);
        Assert.Contains("TargetSceneId", error);
    }
}
