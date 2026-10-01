using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates RevealContent commands. Requires a valid content ID to reveal.
/// </summary>
public sealed class RevealContentValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.RevealContent;

    public string? Validate(AiProposedAction action)
    {
        if (action.ContentId is null || action.ContentId == Guid.Empty)
            return "RevealContent requires a valid ContentId.";

        return null;
    }
}
