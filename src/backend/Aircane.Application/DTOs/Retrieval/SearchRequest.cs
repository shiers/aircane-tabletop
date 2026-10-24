using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Retrieval;

/// <summary>
/// Parameters for a combined keyword + vector retrieval query.
/// </summary>
/// <param name="Scope">
/// A scope resolved from the campaign's game system definition. When set it replaces
/// <paramref name="GameSystem"/> and <paramref name="Ruleset"/> as the filter, and documents with a
/// blank ruleset whose game system matches are included.
/// </param>
public sealed record SearchRequest(
    string Query,
    int TopK = 10,
    string? GameSystem = null,
    string? Ruleset = null,
    SourceType? SourceType = null,
    ContentVisibility? MaxVisibility = null,
    RetrievalScope? Scope = null);
