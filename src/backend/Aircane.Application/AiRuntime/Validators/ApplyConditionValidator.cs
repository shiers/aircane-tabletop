using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates ApplyCondition commands. Requires a valid character and a condition name.
/// </summary>
public sealed class ApplyConditionValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.ApplyCondition;

    public string? Validate(AiProposedAction action)
    {
        if (action.CharacterId is null || action.CharacterId == Guid.Empty)
            return "ApplyCondition requires a valid CharacterId.";

        if (string.IsNullOrWhiteSpace(action.ConditionName))
            return "ApplyCondition requires a ConditionName (e.g., 'Poisoned', 'Prone').";

        return null;
    }
}
