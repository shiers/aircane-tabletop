using Aircane.Application.Abstractions;

namespace Aircane.Application.AiRuntime;

/// <summary>
/// Validates a specific type of AI-proposed state command before execution.
/// Each command type has its own validator that checks required fields and constraints.
/// </summary>
public interface IStateCommandValidator
{
    /// <summary>The action type this validator handles.</summary>
    AiActionType ActionType { get; }

    /// <summary>
    /// Validates the proposed action. Returns null if valid, or an error message if invalid.
    /// </summary>
    string? Validate(AiProposedAction action);
}
