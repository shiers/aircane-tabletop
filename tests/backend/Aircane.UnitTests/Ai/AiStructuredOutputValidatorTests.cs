using Aircane.Application.Abstractions;
using Aircane.Application.Validation;
using FluentValidation.TestHelper;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="AiStructuredOutputValidator"/>, <see cref="AiRulesCitationValidator"/>,
/// and <see cref="AiProposedActionValidator"/>.
/// </summary>
public class AiStructuredOutputValidatorTests
{
    private readonly AiStructuredOutputValidator _validator = new();

    // ── Valid outputs ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_MinimalValidOutput_Passes()
    {
        var output = new AiStructuredOutput { Narration = "The door opens." };
        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_FullValidOutput_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "You enter the chamber.",
            PrivateDmNote = "The trap is armed.",
            RulesCitations =
            [
                new AiRulesCitation
                {
                    SourceDocumentId = Guid.NewGuid().ToString(),
                    ChunkId = Guid.NewGuid().ToString(),
                    Summary = "Trap rules, DMG p.120",
                },
            ],
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    CharacterId = Guid.NewGuid(),
                    Label = "Dexterity Saving Throw",
                    Formula = "1d20+3",
                    Dc = 15,
                    Visibility = "public",
                    Reason = "Avoiding the trap",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Narration validation ──────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyNarration_Fails()
    {
        var output = new AiStructuredOutput { Narration = "" };
        var result = _validator.TestValidate(output);
        result.ShouldHaveValidationErrorFor(x => x.Narration);
    }

    [Fact]
    public void Validate_NarrationExceedsMaxLength_Fails()
    {
        var output = new AiStructuredOutput { Narration = new string('x', 10_001) };
        var result = _validator.TestValidate(output);
        result.ShouldHaveValidationErrorFor(x => x.Narration);
    }

    // ── PrivateDmNote validation ──────────────────────────────────────────────

    [Fact]
    public void Validate_NullPrivateDmNote_Passes()
    {
        var output = new AiStructuredOutput { Narration = "Hello.", PrivateDmNote = null };
        var result = _validator.TestValidate(output);
        result.ShouldNotHaveValidationErrorFor(x => x.PrivateDmNote);
    }

    [Fact]
    public void Validate_PrivateDmNoteExceedsMaxLength_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Hello.",
            PrivateDmNote = new string('x', 5_001),
        };
        var result = _validator.TestValidate(output);
        result.ShouldHaveValidationErrorFor(x => x.PrivateDmNote);
    }

    // ── Citation validation ───────────────────────────────────────────────────

    [Fact]
    public void Validate_CitationWithEmptySourceDocumentId_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Per the rules...",
            RulesCitations =
            [
                new AiRulesCitation
                {
                    SourceDocumentId = "",
                    ChunkId = Guid.NewGuid().ToString(),
                    Summary = "Some rule",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Theory]
    [InlineData("PHB")]
    [InlineData("rules-5e-players-handbook")]
    [InlineData("3f2b8c1e-5d4a-4c7e-9a10-2b6d8e4f7a11")]
    public void Validate_CitationWithReadableOrGuidIds_Passes(string id)
    {
        var output = new AiStructuredOutput
        {
            Narration = "Per the rules...",
            RulesCitations = [new AiRulesCitation { SourceDocumentId = id, ChunkId = id, Summary = "Some rule" }],
        };

        _validator.TestValidate(output).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_CitationWithEmptyChunkId_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Per the rules...",
            RulesCitations = [new AiRulesCitation { SourceDocumentId = "PHB", ChunkId = "", Summary = "Some rule" }],
        };

        _validator.TestValidate(output).ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_CitationWithEmptySummary_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Per the rules...",
            RulesCitations =
            [
                new AiRulesCitation
                {
                    SourceDocumentId = Guid.NewGuid().ToString(),
                    ChunkId = Guid.NewGuid().ToString(),
                    Summary = "",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    // ── RequestRoll action validation ─────────────────────────────────────────

    [Fact]
    public void Validate_RequestRollWithoutFormula_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Roll for stealth.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    Label = "Stealth",
                    Formula = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_RequestRollWithoutLabel_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Roll for stealth.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    Label = null,
                    Formula = "1d20+5",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_RequestRollWithDcOutOfRange_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Roll for stealth.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    Label = "Stealth",
                    Formula = "1d20+5",
                    Dc = 100,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_RequestRollWithNullDc_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Roll for initiative.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    Label = "Initiative",
                    Formula = "1d20+2",
                    Dc = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── ApplyDamage action validation ─────────────────────────────────────────

    [Fact]
    public void Validate_ApplyDamageWithoutAmount_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The goblin strikes.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyDamage,
                    CharacterId = Guid.NewGuid(),
                    Amount = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_ApplyDamageWithZeroAmount_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The goblin strikes.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyDamage,
                    CharacterId = Guid.NewGuid(),
                    Amount = 0,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_ApplyDamageExceedsMax_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The dragon breathes fire.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyDamage,
                    CharacterId = Guid.NewGuid(),
                    Amount = 1001,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_ApplyDamageValidAmount_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The goblin strikes.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyDamage,
                    CharacterId = Guid.NewGuid(),
                    Amount = 8,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── ApplyHealing action validation ────────────────────────────────────────

    [Fact]
    public void Validate_ApplyHealingWithoutAmount_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The cleric heals.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyHealing,
                    CharacterId = Guid.NewGuid(),
                    Amount = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    // ── ApplyCondition action validation ──────────────────────────────────────

    [Fact]
    public void Validate_ApplyConditionWithoutConditionName_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The poison takes effect.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyCondition,
                    CharacterId = Guid.NewGuid(),
                    ConditionName = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_ApplyConditionValid_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The poison takes effect.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.ApplyCondition,
                    CharacterId = Guid.NewGuid(),
                    ConditionName = "Poisoned",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── RemoveCondition action validation ─────────────────────────────────────

    [Fact]
    public void Validate_RemoveConditionWithoutConditionName_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The condition fades.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RemoveCondition,
                    CharacterId = Guid.NewGuid(),
                    ConditionName = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    // ── RevealContent action validation ───────────────────────────────────────

    [Fact]
    public void Validate_RevealContentWithoutContentId_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "A secret is revealed.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RevealContent,
                    ContentId = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    // ── MoveScene action validation ───────────────────────────────────────────

    [Fact]
    public void Validate_MoveSceneWithoutTargetSceneId_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "You move to the next room.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.MoveScene,
                    TargetSceneId = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    // ── Flag action validation ────────────────────────────────────────────────

    [Fact]
    public void Validate_AddQuestFlagWithoutFlagName_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Quest updated.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.AddQuestFlag,
                    FlagName = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_UpdateWorldFlagWithoutFlagName_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "World state changed.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.UpdateWorldFlag,
                    FlagName = null,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_AddQuestFlagValid_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Quest updated.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.AddQuestFlag,
                    FlagName = "rescued_prisoner",
                    FlagValue = "true",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Visibility validation ─────────────────────────────────────────────────

    [Fact]
    public void Validate_InvalidVisibility_Fails()
    {
        var output = new AiStructuredOutput
        {
            Narration = "Roll for stealth.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.RequestRoll,
                    Label = "Stealth",
                    Formula = "1d20+5",
                    Visibility = "invalid_value",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldHaveAnyValidationError();
    }

    // ── Narrate action (no special requirements) ──────────────────────────────

    [Fact]
    public void Validate_NarrateAction_NoSpecialFieldsRequired_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "The wind howls.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.Narrate,
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── AskClarifyingQuestion action (no special requirements) ────────────────

    [Fact]
    public void Validate_AskClarifyingQuestionAction_Passes()
    {
        var output = new AiStructuredOutput
        {
            Narration = "I need more information.",
            ProposedActions =
            [
                new AiProposedAction
                {
                    Type = AiActionType.AskClarifyingQuestion,
                    Reason = "What direction do you go?",
                },
            ],
        };

        var result = _validator.TestValidate(output);
        result.ShouldNotHaveAnyValidationErrors();
    }
}

/// <summary>
/// Validator rules for the solo-adventure action types (required target IDs and length limits).
/// </summary>
public class AiStructuredOutputValidatorAdventureActionTests
{
    private readonly AiProposedActionValidator _validator = new();

    [Fact]
    public void AdvanceScene_WithSceneId_Passes()
        => _validator.TestValidate(new AiProposedAction { Type = AiActionType.AdvanceScene, SceneId = "scene-2" })
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(AiActionType.AdvanceScene, "Scene ID is required")]
    [InlineData(AiActionType.RevealClue, "Clue ID is required")]
    [InlineData(AiActionType.RevealSecret, "Secret ID is required")]
    [InlineData(AiActionType.UpdateNpc, "NPC ID is required")]
    [InlineData(AiActionType.CompleteAdventure, "Outcome notes are required")]
    public void AdventureAction_MissingRequiredField_Fails(AiActionType type, string expectedMessage)
    {
        var result = _validator.TestValidate(new AiProposedAction { Type = type });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(expectedMessage));
    }

    [Fact]
    public void UpdateNpc_WithOnlyOptionalFieldsOmitted_Passes()
        => _validator.TestValidate(new AiProposedAction { Type = AiActionType.UpdateNpc, NpcId = "npc-archivist", IsAlive = false })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CompleteAdventure_OutcomeNotesTooLong_Fails()
        => _validator.TestValidate(new AiProposedAction
            {
                Type = AiActionType.CompleteAdventure,
                OutcomeNotes = new string('x', 5_001),
            })
            .ShouldHaveValidationErrorFor(x => x.OutcomeNotes);
}
