using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Optional filters for listing source documents.
/// </summary>
public sealed record DocumentListRequest(
    SourceType? SourceType = null,
    string? GameSystem = null,
    string? Ruleset = null,
    ImportStatus? ImportStatus = null,
    bool? IsSourceAvailable = null,
    int Page = 1,
    int PageSize = 50);
