using Aircane.Application.DTOs.Adventures;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Analyzes a party of characters to determine their collective capabilities,
/// strengths, and weaknesses for adventure generation and encounter balancing.
/// </summary>
public interface IPartyAnalysisService
{
    /// <summary>
    /// Analyzes the party composition by loading characters and parsing their canonical data.
    /// </summary>
    /// <param name="characterIds">IDs of the characters to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PartyAnalysisResult"/> summarizing the party's capabilities.</returns>
    /// <exception cref="ArgumentException">Thrown when no valid characters are found for the given IDs.</exception>
    Task<PartyAnalysisResult> AnalyzePartyAsync(
        IReadOnlyList<Guid> characterIds,
        CancellationToken cancellationToken = default);
}
