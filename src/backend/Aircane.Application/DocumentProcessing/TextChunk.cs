namespace Aircane.Application.DocumentProcessing;

/// <summary>
/// A single chunk of text produced by splitting a document page or section.
/// </summary>
/// <param name="Text">The chunk text content.</param>
/// <param name="PageNumber">1-based page number this chunk originates from.</param>
/// <param name="SectionTitle">Optional section heading associated with this chunk.</param>
/// <param name="ChunkIndex">
/// Sequential index of this chunk within the whole document (not just the page).
/// </param>
/// <param name="ChunkType">
/// Optional chunk classification, e.g. <c>"stat-block"</c> for a monster/NPC stat block kept whole.
/// Null for ordinary text.
/// </param>
public record TextChunk(
    string Text,
    int PageNumber,
    string? SectionTitle,
    int ChunkIndex,
    string? ChunkType = null);
