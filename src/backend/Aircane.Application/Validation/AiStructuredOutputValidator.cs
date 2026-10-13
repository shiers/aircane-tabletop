using Aircane.Application.Abstractions;
using FluentValidation;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates an <see cref="AiStructuredOutput"/> returned by the AI provider.
/// Ensures required fields are present, action types are valid, UUIDs are well-formed,
/// and numeric values are within reasonable ranges.
/// </summary>
public sealed class AiStructuredOutputValidator : AbstractValidator<AiStructuredOutput>
{
    public AiStructuredOutputValidator()
    {
        RuleFor(x => x.Narration)
            .NotEmpty().WithMessage("Narration is required and cannot be empty.");

        RuleFor(x => x.Narration)
            .MaximumLength(10_000).WithMessage("Narration must not exceed 10,000 characters.");

        RuleFor(x => x.PrivateDmNote)
            .MaximumLength(5_000).WithMessage("Private DM note must not exceed 5,000 characters.")
            .When(x => x.PrivateDmNote is not null);

        RuleForEach(x => x.RulesCitations)
            .SetValidator(new AiRulesCitationValidator());

        RuleForEach(x => x.ProposedActions)
            .SetValidator(new AiProposedActionValidator());
    }
}

/// <summary>
/// Validates an <see cref="AiRulesCitation"/> entry.
/// </summary>
public sealed class AiRulesCitationValidator : AbstractValidator<AiRulesCitation>
{
    public AiRulesCitationValidator()
    {
        // Free text: the model may cite "PHB" as readily as a GUID. Only presence and size are checked.
        RuleFor(x => x.SourceDocumentId)
            .NotEmpty().WithMessage("Source document ID is required.")
            .MaximumLength(200).WithMessage("Source document ID must not exceed 200 characters.");

        RuleFor(x => x.ChunkId)
            .NotEmpty().WithMessage("Chunk ID is required.")
            .MaximumLength(200).WithMessage("Chunk ID must not exceed 200 characters.");

        RuleFor(x => x.Summary)
            .NotEmpty().WithMessage("Citation summary is required.")
            .MaximumLength(500).WithMessage("Citation summary must not exceed 500 characters.");
    }
}

/// <summary>
/// Validates an <see cref="AiProposedAction"/> entry.
/// Checks that the action type is valid and that type-specific fields are present
/// and within reasonable ranges.
/// </summary>
public sealed class AiProposedActionValidator : AbstractValidator<AiProposedAction>
{
    private static readonly string[] ValidVisibilities = ["public", "private"];

    public AiProposedActionValidator()
    {
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Action type must be a valid AiActionType value.");

        RuleFor(x => x.Label)
            .MaximumLength(200).WithMessage("Action label must not exceed 200 characters.")
            .When(x => x.Label is not null);

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Action reason must not exceed 500 characters.")
            .When(x => x.Reason is not null);

        RuleFor(x => x.Visibility)
            .Must(v => ValidVisibilities.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Visibility must be 'public' or 'private'.")
            .When(x => x.Visibility is not null);

        // RequestRoll: formula and label are expected
        When(x => x.Type == AiActionType.RequestRoll, () =>
        {
            RuleFor(x => x.Formula)
                .NotEmpty().WithMessage("Formula is required for RequestRoll actions.");

            RuleFor(x => x.Label)
                .NotEmpty().WithMessage("Label is required for RequestRoll actions.");

            RuleFor(x => x.Dc)
                .InclusiveBetween(1, 50).WithMessage("DC must be between 1 and 50.")
                .When(x => x.Dc is not null);
        });

        // ApplyDamage: amount is required and must be positive
        When(x => x.Type == AiActionType.ApplyDamage, () =>
        {
            RuleFor(x => x.Amount)
                .NotNull().WithMessage("Amount is required for ApplyDamage actions.")
                .GreaterThan(0).WithMessage("Damage amount must be greater than 0.")
                .LessThanOrEqualTo(1000).WithMessage("Damage amount must not exceed 1000.");

            RuleFor(x => x.CharacterId)
                .NotNull().WithMessage("Character ID is required for ApplyDamage actions.");

            RuleFor(x => x.CharacterId)
                .NotEqual(Guid.Empty).WithMessage("Character ID must be a valid non-empty UUID.")
                .When(x => x.CharacterId is not null);
        });

        // ApplyHealing: amount is required and must be positive
        When(x => x.Type == AiActionType.ApplyHealing, () =>
        {
            RuleFor(x => x.Amount)
                .NotNull().WithMessage("Amount is required for ApplyHealing actions.")
                .GreaterThan(0).WithMessage("Healing amount must be greater than 0.")
                .LessThanOrEqualTo(1000).WithMessage("Healing amount must not exceed 1000.");

            RuleFor(x => x.CharacterId)
                .NotNull().WithMessage("Character ID is required for ApplyHealing actions.");

            RuleFor(x => x.CharacterId)
                .NotEqual(Guid.Empty).WithMessage("Character ID must be a valid non-empty UUID.")
                .When(x => x.CharacterId is not null);
        });

        // ApplyCondition: condition name is required
        When(x => x.Type == AiActionType.ApplyCondition, () =>
        {
            RuleFor(x => x.ConditionName)
                .NotEmpty().WithMessage("Condition name is required for ApplyCondition actions.");

            RuleFor(x => x.CharacterId)
                .NotNull().WithMessage("Character ID is required for ApplyCondition actions.");

            RuleFor(x => x.CharacterId)
                .NotEqual(Guid.Empty).WithMessage("Character ID must be a valid non-empty UUID.")
                .When(x => x.CharacterId is not null);
        });

        // RemoveCondition: condition name is required
        When(x => x.Type == AiActionType.RemoveCondition, () =>
        {
            RuleFor(x => x.ConditionName)
                .NotEmpty().WithMessage("Condition name is required for RemoveCondition actions.");

            RuleFor(x => x.CharacterId)
                .NotNull().WithMessage("Character ID is required for RemoveCondition actions.");

            RuleFor(x => x.CharacterId)
                .NotEqual(Guid.Empty).WithMessage("Character ID must be a valid non-empty UUID.")
                .When(x => x.CharacterId is not null);
        });

        // RevealContent: content ID is required
        When(x => x.Type == AiActionType.RevealContent, () =>
        {
            RuleFor(x => x.ContentId)
                .NotNull().WithMessage("Content ID is required for RevealContent actions.");

            RuleFor(x => x.ContentId)
                .NotEqual(Guid.Empty).WithMessage("Content ID must be a valid non-empty UUID.")
                .When(x => x.ContentId is not null);
        });

        // MoveScene: target scene ID is required
        When(x => x.Type == AiActionType.MoveScene, () =>
        {
            RuleFor(x => x.TargetSceneId)
                .NotNull().WithMessage("Target scene ID is required for MoveScene actions.");

            RuleFor(x => x.TargetSceneId)
                .NotEqual(Guid.Empty).WithMessage("Target scene ID must be a valid non-empty UUID.")
                .When(x => x.TargetSceneId is not null);
        });

        // Solo adventure actions: the target IDs are required. Whether they exist in the adventure
        // is checked when the action is applied (AdventureSessionService).
        When(x => x.Type == AiActionType.AdvanceScene, () =>
        {
            RuleFor(x => x.SceneId)
                .NotEmpty().WithMessage("Scene ID is required for AdvanceScene actions.")
                .MaximumLength(100).WithMessage("Scene ID must not exceed 100 characters.");

            RuleFor(x => x.Narration)
                .MaximumLength(10_000).WithMessage("Transition narration must not exceed 10,000 characters.")
                .When(x => x.Narration is not null);
        });

        When(x => x.Type == AiActionType.RevealClue, () =>
        {
            RuleFor(x => x.ClueId)
                .NotEmpty().WithMessage("Clue ID is required for RevealClue actions.")
                .MaximumLength(100).WithMessage("Clue ID must not exceed 100 characters.");
        });

        When(x => x.Type == AiActionType.RevealSecret, () =>
        {
            RuleFor(x => x.SecretId)
                .NotEmpty().WithMessage("Secret ID is required for RevealSecret actions.")
                .MaximumLength(100).WithMessage("Secret ID must not exceed 100 characters.");
        });

        When(x => x.Type == AiActionType.UpdateNpc, () =>
        {
            RuleFor(x => x.NpcId)
                .NotEmpty().WithMessage("NPC ID is required for UpdateNpc actions.")
                .MaximumLength(100).WithMessage("NPC ID must not exceed 100 characters.");

            RuleFor(x => x.Notes)
                .MaximumLength(2_000).WithMessage("NPC notes must not exceed 2,000 characters.")
                .When(x => x.Notes is not null);
        });

        When(x => x.Type == AiActionType.CompleteAdventure, () =>
        {
            RuleFor(x => x.OutcomeNotes)
                .NotEmpty().WithMessage("Outcome notes are required for CompleteAdventure actions.")
                .MaximumLength(5_000).WithMessage("Outcome notes must not exceed 5,000 characters.");
        });

        // AddQuestFlag / UpdateWorldFlag: flag name is required
        When(x => x.Type is AiActionType.AddQuestFlag or AiActionType.UpdateWorldFlag, () =>
        {
            RuleFor(x => x.FlagName)
                .NotEmpty().WithMessage("Flag name is required for flag actions.");

            RuleFor(x => x.FlagName)
                .MaximumLength(100).WithMessage("Flag name must not exceed 100 characters.")
                .When(x => x.FlagName is not null);
        });
    }
}
