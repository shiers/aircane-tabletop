using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Implements condition registration and lookup against a game system's condition set.
/// Supports freeform text conditions when no condition set is defined (empty list).
/// </summary>
public class ConditionRegistry : IConditionRegistry
{
    /// <inheritdoc />
    public IReadOnlyList<ConditionDefinition> GetConditions(IReadOnlyList<ConditionDefinition> conditionSet)
    {
        ArgumentNullException.ThrowIfNull(conditionSet);
        return conditionSet;
    }

    /// <inheritdoc />
    public bool IsValidCondition(IReadOnlyList<ConditionDefinition> conditionSet, string conditionName)
    {
        ArgumentNullException.ThrowIfNull(conditionSet);

        if (string.IsNullOrWhiteSpace(conditionName))
            return false;

        // Freeform mode: empty condition set means any condition name is valid
        if (conditionSet.Count == 0)
            return true;

        // Check if the condition name exists in the set (case-insensitive)
        return conditionSet.Any(c =>
            string.Equals(c.Name, conditionName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public ConditionDefinition? GetCondition(IReadOnlyList<ConditionDefinition> conditionSet, string conditionName)
    {
        ArgumentNullException.ThrowIfNull(conditionSet);

        if (string.IsNullOrWhiteSpace(conditionName))
            return null;

        // In freeform mode (empty set), no mechanical effects are available
        if (conditionSet.Count == 0)
            return null;

        return conditionSet.FirstOrDefault(c =>
            string.Equals(c.Name, conditionName, StringComparison.OrdinalIgnoreCase));
    }
}
