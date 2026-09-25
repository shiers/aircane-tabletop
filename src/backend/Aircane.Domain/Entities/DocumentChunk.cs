using Aircane.Domain.Common;
using Aircane.Domain.Enums;
using Pgvector;

namespace Aircane.Domain.Entities;

/// <summary>
/// A searchable text chunk extracted from a SourceDocument.
/// The Embedding property uses the pgvector Vector type for native vector storage and similarity search.
/// </summary>
public class DocumentChunk : EntityBase
{
    public Guid SourceDocumentId { get; init; }
    public int ChunkIndex { get; init; }
    public string Text { get; init; }
    public int? PageNumber { get; init; }
    public string? SectionTitle { get; init; }
    public string? ChunkType { get; init; }
    public ContentVisibility Visibility { get; set; }
    public string MetadataJson { get; set; }

    /// <summary>
    /// Embedding vector stored via pgvector. Dimension is 768 (nomic-embed-text default).
    /// Null until the document import job generates and stores the embedding.
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>
    /// Provenance: the embedding provider that produced <see cref="Embedding"/> (e.g. "Ollama").
    /// Null when no embedding has been generated, or for embeddings created before provenance
    /// tracking was introduced (treated as compatible with the active provider).
    /// </summary>
    public string? EmbeddingProvider { get; set; }

    /// <summary>
    /// Provenance: the embedding model that produced <see cref="Embedding"/> (e.g. "nomic-embed-text").
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// Provenance: the dimensionality of <see cref="Embedding"/> (e.g. 768). Null when no
    /// embedding has been generated or provenance is unknown.
    /// </summary>
    public int? EmbeddingDimensions { get; set; }

    public DocumentChunk(
        Guid sourceDocumentId,
        int chunkIndex,
        string text,
        int? pageNumber = null,
        string? sectionTitle = null,
        string? chunkType = null,
        ContentVisibility visibility = ContentVisibility.DMOnly,
        string metadataJson = "{}")
    {
        SourceDocumentId = sourceDocumentId;
        ChunkIndex = chunkIndex;
        Text = text;
        PageNumber = pageNumber;
        SectionTitle = sectionTitle;
        ChunkType = chunkType;
        Visibility = visibility;
        MetadataJson = metadataJson;
    }

    // EF Core constructor
    private DocumentChunk() : base()
    {
        Text = string.Empty;
        MetadataJson = "{}";
    }
}
