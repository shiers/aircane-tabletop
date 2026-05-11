using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Represents a registered watched folder returned to callers.
/// </summary>
public sealed record WatchedFolderDto(
    Guid Id,
    string DisplayName,
    string AbsolutePath,
    SourceType DefaultSourceType,
    string? DefaultGameSystem,
    string? DefaultRuleset,
    DateTimeOffset? LastScannedAt,
    DateTimeOffset CreatedAt);
