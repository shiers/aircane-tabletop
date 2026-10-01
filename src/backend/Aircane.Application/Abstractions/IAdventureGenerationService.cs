using Aircane.Application.DTOs.Adventures;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Service that orchestrates the staged adventure generation pipeline.
/// Each stage can be called independently for regeneration of individual sections.
/// </summary>
public interface IAdventureGenerationService
{
    /// <summary>
    /// Runs the full generation pipeline from pitch through clues/treasure.
    /// </summary>
    /// <param name="request">The adventure generation parameters.</param>
    /// <param name="partyAnalysis">Optional party analysis result for tailoring content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete generated adventure.</returns>
    Task<GeneratedAdventureDto> GenerateAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the pitch stage for an existing adventure.
    /// </summary>
    Task<AdventurePitch> RegeneratePitchAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the outline stage for an existing adventure.
    /// </summary>
    Task<AdventureOutline> RegenerateOutlineAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the scenes stage for an existing adventure.
    /// </summary>
    Task<IReadOnlyList<GeneratedScene>> RegenerateScenesAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        AdventureOutline outline,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the NPCs stage for an existing adventure.
    /// </summary>
    Task<IReadOnlyList<GeneratedNpc>> RegenerateNpcsAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the encounters stage for an existing adventure.
    /// </summary>
    Task<IReadOnlyList<GeneratedEncounter>> RegenerateEncountersAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the treasure stage for an existing adventure.
    /// </summary>
    Task<AdventureTreasure> RegenerateTreasureAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        IReadOnlyList<GeneratedEncounter> encounters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates only the clues/secrets stage for an existing adventure.
    /// </summary>
    Task<AdventureClues> RegenerateCluesAsync(
        GenerateAdventureRequest request,
        PartyAnalysisResult? partyAnalysis,
        AdventurePitch pitch,
        IReadOnlyList<GeneratedScene> scenes,
        IReadOnlyList<GeneratedNpc> npcs,
        CancellationToken cancellationToken = default);
}
