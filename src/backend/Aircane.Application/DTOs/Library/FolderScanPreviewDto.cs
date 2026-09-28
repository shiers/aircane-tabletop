using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Library;

/// <summary>
/// The result of analyzing a watched folder without importing anything. Presented to the host so
/// they can review discovered files, resolve duplicates, and choose what to import.
/// </summary>
/// <param name="FolderId">The watched folder that was analyzed.</param>
/// <param name="FilesFound">Total number of importable files discovered.</param>
/// <param name="Candidates">Per-file analysis, one entry per discovered file.</param>
/// <param name="AnalyzedAt">UTC timestamp when the analysis ran.</param>
public sealed record FolderScanPreviewDto(
    Guid FolderId,
    int FilesFound,
    IReadOnlyList<ScanCandidateDto> Candidates,
    DateTimeOffset AnalyzedAt);

/// <summary>
/// A non-authoritative reason a candidate is flagged for the host's attention.
/// </summary>
public enum ScanCandidateFlag
{
    /// <summary>Another discovered file normalizes to the same work (e.g. a different OCR/edition scan).</summary>
    DuplicateVariant,

    /// <summary>A document with the same normalized identity is already in the library.</summary>
    AlreadyImported,

    /// <summary>The filename looks like a non-rules asset (map pack, DM screen, tokens, etc.).</summary>
    LikelyNotRules,

    /// <summary>
    /// The file's content hash exactly matches an already-indexed document — a stronger signal
    /// than <see cref="AlreadyImported"/>/<see cref="DuplicateVariant"/> (same bytes, possibly a
    /// different filename).
    /// </summary>
    ExactDuplicate,
}

/// <summary>
/// Analysis of a single discovered file. All suggestions are hints the host may override; nothing
/// is imported until the host confirms a selection.
/// </summary>
/// <param name="SourcePath">Absolute path used to import/open the file. Stable identifier for the candidate.</param>
/// <param name="FileName">Bare filename including extension.</param>
/// <param name="SuggestedTitle">Cleaned-up title suggestion derived from the filename.</param>
/// <param name="SuggestedRuleset">Year detected in the filename (e.g. "2014"), or null if none.</param>
/// <param name="SizeBytes">File size in bytes, if known.</param>
/// <param name="DedupKey">Normalized key grouping near-duplicate variants of the same work.</param>
/// <param name="Flags">Zero or more advisory flags (duplicate, already imported, likely-not-rules).</param>
/// <param name="Reason">Human-readable explanation of the flags, for display.</param>
/// <param name="AlreadyImported">True when a document with this dedup identity already exists in the library.</param>
public sealed record ScanCandidateDto(
    string SourcePath,
    string FileName,
    string SuggestedTitle,
    string? SuggestedRuleset,
    long? SizeBytes,
    string DedupKey,
    IReadOnlyList<ScanCandidateFlag> Flags,
    string? Reason,
    bool AlreadyImported);
