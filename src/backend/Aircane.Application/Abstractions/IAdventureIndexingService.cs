namespace Aircane.Application.Abstractions;

/// <summary>
/// Converts generated adventure content into searchable DocumentChunks,
/// enabling the RAG pipeline to retrieve adventure context during play.
/// </summary>
public interface IAdventureIndexingService
{
    /// <summary>
    /// Indexes a generated adventure by converting its scenes, NPCs, encounters, and clues
    /// into DocumentChunks with embeddings. Creates a SourceDocument record with SourceType.Generated
    /// and stores all chunks in the same table used by imported documents.
    /// </summary>
    /// <param name="adventureId">The ID of the generated adventure to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the created SourceDocument record.</returns>
    Task<Guid> IndexAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default);
}
