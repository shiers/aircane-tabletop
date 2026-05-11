using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Request to update an existing watched folder's settings.
/// All fields are optional; only non-null values are applied.
/// </summary>
public sealed record UpdateFolderRequest(
    string? DisplayName = null,
    string? AbsolutePath = null,
    SourceType? DefaultSourceType = null,
    string? DefaultGameSystem = null,
    string? DefaultRuleset = null);
