using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates ApplyHealing commands. Requires a valid character and a positive healing amount.
/// </summary>
public sealed class ApplyHealingValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.ApplyHealing;

    public string? Validate(AiProposedAction action)
    {
        if (action.CharacterId is null || action.CharacterId == Guid.Empty)
            return "ApplyHealing requires a valid CharacterId.";

        if (action.Amount is null || action.Amount <= 0)
            return "ApplyHealing requires a positive Amount.";

        return null;
    }
}
