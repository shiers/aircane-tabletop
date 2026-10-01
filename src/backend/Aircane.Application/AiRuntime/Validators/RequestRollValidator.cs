using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates RequestRoll commands. Requires a character target, a label, and a dice formula.
/// </summary>
public sealed class RequestRollValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.RequestRoll;

    public string? Validate(AiProposedAction action)
    {
        if (action.CharacterId is null || action.CharacterId == Guid.Empty)
            return "RequestRoll requires a valid CharacterId.";

        if (string.IsNullOrWhiteSpace(action.Label))
            return "RequestRoll requires a Label describing the roll.";

        if (string.IsNullOrWhiteSpace(action.Formula))
            return "RequestRoll requires a dice Formula (e.g., '1d20+5').";

        return null;
    }
}
