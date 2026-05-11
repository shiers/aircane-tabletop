using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Represents source document metadata returned to callers.
/// Does not include the file path or binary content.
/// </summary>
public sealed record SourceDocumentDto(
    Guid Id,
    string Title,
    string OriginalFileName,
    SourceType SourceType,
    SourceMode SourceMode,
    string GameSystem,
    string Ruleset,
    ContentVisibility Visibility,
    ImportStatus ImportStatus,
    bool IsSourceAvailable,
    Guid? WatchedFolderId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Tags);
