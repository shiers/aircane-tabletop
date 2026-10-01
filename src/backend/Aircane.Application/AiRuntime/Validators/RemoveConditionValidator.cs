using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates RemoveCondition commands. Requires a valid character and a condition name.
/// </summary>
public sealed class RemoveConditionValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.RemoveCondition;

    public string? Validate(AiProposedAction action)
    {
        if (action.CharacterId is null || action.CharacterId == Guid.Empty)
            return "RemoveCondition requires a valid CharacterId.";

        if (string.IsNullOrWhiteSpace(action.ConditionName))
            return "RemoveCondition requires a ConditionName to remove.";

        return null;
    }
}
