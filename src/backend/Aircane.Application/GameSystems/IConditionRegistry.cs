using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Registry for game system conditions. Validates condition names against the active set
/// and provides condition definitions with their mechanical effects.
/// </summary>
public interface IConditionRegistry
{
    /// <summary>
    /// Returns all conditions from the provided condition set.
    /// An empty condition set indicates freeform mode (any condition name is valid).
    /// </summary>
    IReadOnlyList<ConditionDefinition> GetConditions(IReadOnlyList<ConditionDefinition> conditionSet);

    /// <summary>
    /// Validates that a condition name exists in the provided condition set.
    /// Returns true for any name when the condition set is empty (freeform mode).
    /// </summary>
    bool IsValidCondition(IReadOnlyList<ConditionDefinition> conditionSet, string conditionName);

    /// <summary>
    /// Returns the condition definition for the given name, or null if not found.
    /// In freeform mode (empty set), returns null (no mechanical effects available).
    /// </summary>
    ConditionDefinition? GetCondition(IReadOnlyList<ConditionDefinition> conditionSet, string conditionName);
}
