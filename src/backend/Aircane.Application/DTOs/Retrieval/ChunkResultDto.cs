using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Retrieval;

/// <summary>
/// A single retrieved document chunk with relevance metadata.
/// </summary>
public sealed record ChunkResultDto(
    Guid ChunkId,
    Guid SourceDocumentId,
    string SourceDocumentTitle,
    int ChunkIndex,
    string Text,
    int? PageNumber,
    string? SectionTitle,
    ContentVisibility Visibility,
    double? Score);
