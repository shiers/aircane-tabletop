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
        // CharacterId is optional: players may track their character outside Aircane (pen and paper,
        // D&D Beyond, Pathbuilder...). Without one the roll is simply asked for in the narration and
        // the player reports the result in their next action.

        if (string.IsNullOrWhiteSpace(action.Label))
            return "RequestRoll requires a Label describing the roll.";

        if (string.IsNullOrWhiteSpace(action.Formula))
            return "RequestRoll requires a dice Formula (e.g., '1d20+5').";

        return null;
    }
}
