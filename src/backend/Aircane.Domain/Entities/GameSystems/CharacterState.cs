namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// Represents the current state of a character for attribute lookups during resolution.
/// </summary>
public record CharacterState
{
    /// <summary>Character attributes keyed by name (e.g., "ac", "dc", "defender_total").</summary>
    public IReadOnlyDictionary<string, int> Attributes { get; init; } = new Dictionary<string, int>();
}
