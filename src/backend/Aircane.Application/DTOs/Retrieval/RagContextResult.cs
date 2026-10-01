namespace Aircane.Application.DTOs.Retrieval;

/// <summary>
/// The assembled RAG context ready for inclusion in an AI prompt.
/// </summary>
public sealed record RagContextResult(
    /// <summary>The compact prompt context string containing retrieved rules/content with citations.</summary>
    string ContextText,

    /// <summary>Individual citations for attribution and traceability.</summary>
    IReadOnlyList<RagCitation> Citations,

    /// <summary>Total number of chunks retrieved before budget trimming.</summary>
    int TotalChunksRetrieved,

    /// <summary>Number of chunks included in the final context (after budget trimming).</summary>
    int ChunksIncluded);

/// <summary>
/// A citation reference for a chunk included in the RAG context.
/// </summary>
public sealed record RagCitation(
    /// <summary>The chunk ID for traceability.</summary>
    Guid ChunkId,

    /// <summary>The source document ID.</summary>
    Guid SourceDocumentId,

    /// <summary>The source document title for display.</summary>
    string SourceDocumentTitle,

    /// <summary>Page number within the source document, if available.</summary>
    int? PageNumber,

    /// <summary>Section title within the source document, if available.</summary>
    string? SectionTitle);
