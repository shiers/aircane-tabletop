using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Retrieval;

/// <summary>
/// Parameters for a combined keyword + vector retrieval query.
/// </summary>
public sealed record SearchRequest(
    string Query,
    int TopK = 10,
    string? GameSystem = null,
    string? Ruleset = null,
    SourceType? SourceType = null,
    ContentVisibility? MaxVisibility = null);
