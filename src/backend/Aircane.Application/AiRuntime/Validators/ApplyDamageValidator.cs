using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates ApplyDamage commands. Requires a valid character and a positive damage amount.
/// </summary>
public sealed class ApplyDamageValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.ApplyDamage;

    public string? Validate(AiProposedAction action)
    {
        if (action.CharacterId is null || action.CharacterId == Guid.Empty)
            return "ApplyDamage requires a valid CharacterId.";

        if (action.Amount is null || action.Amount <= 0)
            return "ApplyDamage requires a positive Amount.";

        return null;
    }
}
