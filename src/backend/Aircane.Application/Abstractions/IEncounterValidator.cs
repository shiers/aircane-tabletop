using Aircane.Application.DTOs.Adventures;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Validates generated encounters against party size and level to flag
/// obviously unsuitable difficulty levels.
/// </summary>
public interface IEncounterValidator
{
    /// <summary>
    /// Validates a generated encounter against the party's size and average level.
    /// </summary>
    /// <param name="encounter">The generated encounter to validate.</param>
    /// <param name="partySize">Number of characters in the party.</param>
    /// <param name="averageLevel">Average character level of the party.</param>
    /// <returns>Validation result with estimated difficulty and any warnings.</returns>
    EncounterValidationResult ValidateEncounter(
        GeneratedEncounter encounter,
        int partySize,
        int averageLevel);
}
