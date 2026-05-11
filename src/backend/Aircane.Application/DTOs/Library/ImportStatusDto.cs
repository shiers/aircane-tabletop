using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Represents the current import job status for a source document.
/// </summary>
public sealed record ImportStatusDto(
    Guid DocumentId,
    ImportStatus Status,
    int? ProgressPercent,
    string? ErrorMessage,
    DateTimeOffset UpdatedAt,
    bool IsSourceAvailable = true);
