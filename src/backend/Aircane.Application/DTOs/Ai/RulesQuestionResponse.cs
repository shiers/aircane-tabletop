namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// Response to a rules question, including the answer, citations, and source support indicator.
/// </summary>
public sealed record RulesQuestionResponse(
    /// <summary>The AI-generated answer to the rules question.</summary>
    string Answer,

    /// <summary>Citations from the retrieved source documents.</summary>
    IReadOnlyList<RulesQuestionCitation> Citations,

    /// <summary>
    /// Indicates whether the answer is supported by retrieved source documents.
    /// When false, the AI could not find relevant sources and the answer may be less reliable.
    /// </summary>
    bool HasSourceSupport);

/// <summary>
/// A citation reference included in a rules question answer.
/// </summary>
public sealed record RulesQuestionCitation(
    /// <summary>The source document title.</summary>
    string SourceTitle,

    /// <summary>Page number within the source document, if available.</summary>
    int? PageNumber,

    /// <summary>Section title within the source document, if available.</summary>
    string? SectionTitle,

    /// <summary>The chunk ID for traceability.</summary>
    Guid ChunkId,

    /// <summary>
    /// Machine-readable license key of the cited source (e.g. "cc-by-4.0"), when the chunk
    /// belongs to a built-in document. Null for user-imported sources.
    /// </summary>
    string? LicenseKey = null,

    /// <summary>Human-readable license name of the cited source, when built-in. Null otherwise.</summary>
    string? LicenseDisplayName = null,

    /// <summary>
    /// Short-form attribution notice for the cited source, when built-in. Null otherwise.
    /// </summary>
    string? AttributionText = null);
