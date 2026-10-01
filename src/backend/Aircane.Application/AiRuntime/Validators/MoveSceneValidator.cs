using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime.Validators;

/// <summary>
/// Validates MoveScene commands. Requires a valid target scene ID.
/// </summary>
public sealed class MoveSceneValidator : IStateCommandValidator
{
    public AiActionType ActionType => AiActionType.MoveScene;

    public string? Validate(AiProposedAction action)
    {
        if (action.TargetSceneId is null || action.TargetSceneId == Guid.Empty)
            return "MoveScene requires a valid TargetSceneId.";

        return null;
    }
}
