using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Request to upload and classify a new source document.
/// The file content is provided as a stream by the caller.
/// </summary>
public sealed record UploadDocumentRequest(
    string Title,
    string OriginalFileName,
    SourceType SourceType,
    string GameSystem,
    string Ruleset,
    ContentVisibility Visibility,
    Stream FileContent,
    string? ContentType = null);
