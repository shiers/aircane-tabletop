namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Envelope returned by the source-adapter import path (extended POST /api/characters/import and
/// the D&amp;D Beyond URL import). Carries the detected-source / confidence / ruleset ENVELOPE
/// signals plus the existing <see cref="CharacterFieldReviewDto"/> that drives the review UI.
/// </summary>
public sealed record SourceImportResponse(
    /// <summary>
    /// The source format name the detector resolved (or the pinned source), e.g. "PathbuilderTwo".
    /// Emitted as a string so the wire contract is stable regardless of API-wide JSON enum settings.
    /// </summary>
    string DetectedSource,
    /// <summary>"high" or "low".</summary>
    string Confidence,
    /// <summary>True when the source was Unknown or the mapper's confidence was low.</summary>
    bool RequiresSourceConfirmation,
    /// <summary>Detected ruleset ("2014"/"2024"/"Remaster") or null when none is implied.</summary>
    string? Ruleset,
    /// <summary>True when the detected ruleset should be confirmed before saving.</summary>
    bool RulesetRequiresConfirmation,
    /// <summary>The review payload the frontend renders.</summary>
    CharacterFieldReviewDto Review);
