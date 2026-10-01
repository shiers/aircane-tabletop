using Aircane.Application.DocumentProcessing;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Splits a block of text into overlapping chunks suitable for embedding and retrieval.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Splits <paramref name="text"/> into one or more <see cref="TextChunk"/> records.
    /// </summary>
    /// <param name="text">The text to chunk. Returns an empty list when null or whitespace.</param>
    /// <param name="pageNumber">1-based page number to associate with every produced chunk.</param>
    /// <param name="startingChunkIndex">
    /// The document-level chunk index to assign to the first chunk produced.
    /// Subsequent chunks increment from this value.
    /// </param>
    /// <param name="sectionTitle">Optional section heading to attach to every produced chunk.</param>
    /// <returns>
    /// An ordered, read-only list of chunks. Empty when <paramref name="text"/> is empty or whitespace.
    /// </returns>
    IReadOnlyList<TextChunk> Chunk(
        string text,
        int pageNumber,
        int startingChunkIndex = 0,
        string? sectionTitle = null);
}
