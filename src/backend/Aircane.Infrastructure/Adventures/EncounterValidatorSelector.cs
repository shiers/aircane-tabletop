using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Selects between the D&amp;D 5e and PF2e encounter validators based on a game-system
/// identifier or name. Defaults to the D&amp;D 5e validator for unknown systems.
/// </summary>
public sealed class EncounterValidatorSelector : IEncounterValidatorSelector
{
    private readonly Dnd5eEncounterValidator _dnd5e;
    private readonly Pf2eEncounterValidator _pf2e;

    public EncounterValidatorSelector(
        Dnd5eEncounterValidator dnd5e,
        Pf2eEncounterValidator pf2e)
    {
        _dnd5e = dnd5e;
        _pf2e = pf2e;
    }

    /// <inheritdoc />
    public IEncounterValidator ForGameSystem(string? gameSystemIdentifierOrName)
    {
        return IsPathfinder2e(gameSystemIdentifierOrName) ? _pf2e : _dnd5e;
    }

    /// <summary>
    /// Recognizes the PF2e Remaster seed by its canonical identifier or common name variants.
    /// </summary>
    internal static bool IsPathfinder2e(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "pathfinder-2e-remaster"
            || normalized.Contains("pathfinder 2")
            || normalized.Contains("pathfinder second")
            || normalized is "pf2e" or "pf2" or "pf2e remaster";
    }
}
