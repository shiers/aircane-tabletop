using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Request to update the classification metadata of an existing source document.
/// All fields are optional; only non-null values are applied.
/// </summary>
public sealed record UpdateClassificationRequest(
    string? Title = null,
    SourceType? SourceType = null,
    string? GameSystem = null,
    string? Ruleset = null,
    IReadOnlyList<string>? Tags = null);
