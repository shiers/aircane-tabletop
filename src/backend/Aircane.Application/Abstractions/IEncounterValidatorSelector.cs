namespace Aircane.Application.Abstractions;

/// <summary>
/// Selects the appropriate <see cref="IEncounterValidator"/> for a game system so encounter
/// difficulty is judged with system-correct rules (e.g. the D&amp;D 5e XP table vs the PF2e
/// creature-level budget).
/// </summary>
public interface IEncounterValidatorSelector
{
    /// <summary>
    /// Returns the validator matching the given game-system identifier or name. Falls back to the
    /// default (D&amp;D 5e) validator when the system is unknown.
    /// </summary>
    /// <param name="gameSystemIdentifierOrName">
    /// A game-system definition identifier (e.g. "pathfinder-2e-remaster") or display name
    /// (e.g. "Pathfinder 2e"). Matching is case-insensitive and tolerant of common variants.
    /// </param>
    IEncounterValidator ForGameSystem(string? gameSystemIdentifierOrName);
}
