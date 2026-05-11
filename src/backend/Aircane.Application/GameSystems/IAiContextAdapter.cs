using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Adapts game system definitions into AI-consumable context, formats roll requests
/// using the active convention, and validates AI-proposed actions.
/// </summary>
public interface IAiContextAdapter
{
    /// <summary>
    /// Builds the game-system-specific portion of the AI system prompt.
    /// </summary>
    /// <param name="definition">The active game system definition.</param>
    /// <returns>A formatted string for inclusion in the AI system prompt.</returns>
    string BuildSystemContext(GameSystemDefinition definition);

    /// <summary>
    /// Formats a roll request using the active system's dice convention notation.
    /// </summary>
    /// <param name="convention">The dice convention to use for formatting.</param>
    /// <param name="label">A human-readable label for the roll (e.g., "Strength Check").</param>
    /// <param name="formula">The dice formula expression.</param>
    /// <param name="rule">Optional resolution rule for context.</param>
    /// <returns>A formatted roll request.</returns>
    AiRollRequest FormatRollRequest(
        DiceConvention convention,
        string label,
        string formula,
        ResolutionRule? rule = null);

    /// <summary>
    /// Validates that AI-proposed actions use valid condition names and action types
    /// for the active game system.
    /// </summary>
    /// <param name="actions">The proposed actions to validate.</param>
    /// <param name="definition">The active game system definition.</param>
    /// <returns>Validation result with any errors.</returns>
    AiContextValidationResult ValidateProposedActions(
        IReadOnlyList<AiProposedAction> actions,
        GameSystemDefinition definition);
}

/// <summary>
/// A formatted roll request for the AI to present to the player/host.
/// </summary>
public record AiRollRequest
{
    /// <summary>Human-readable label for the roll (e.g., "Strength Check").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The dice formula in the active convention's notation.</summary>
    public string Formula { get; init; } = string.Empty;

    /// <summary>The convention type used for this roll (e.g., "DicePoolSuccess", "SingleDieModifier").</summary>
    public string ConventionType { get; init; } = string.Empty;

    /// <summary>Description of how the roll is resolved, if a resolution rule is provided.</summary>
    public string? ResolutionDescription { get; init; }
}

/// <summary>
/// An action proposed by the AI that needs validation against the active game system.
/// </summary>
public record AiProposedAction
{
    /// <summary>The type of action (e.g., "attack", "cast_spell", "apply_condition").</summary>
    public string ActionType { get; init; } = string.Empty;

    /// <summary>Condition name if the action applies a condition.</summary>
    public string? ConditionName { get; init; }

    /// <summary>Roll formula if the action requires a roll.</summary>
    public string? RollFormula { get; init; }
}

/// <summary>
/// Result of validating AI-proposed actions against the active game system.
/// </summary>
public record AiContextValidationResult
{
    /// <summary>Whether all proposed actions are valid.</summary>
    public bool IsValid { get; init; }

    /// <summary>List of validation errors for invalid actions.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
