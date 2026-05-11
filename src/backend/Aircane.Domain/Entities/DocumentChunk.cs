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
