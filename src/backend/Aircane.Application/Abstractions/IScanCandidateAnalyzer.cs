using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Analyzes files discovered in a watched folder and produces advisory <see cref="ScanCandidateDto"/>
/// entries: cleaned-up title/ruleset suggestions, a dedup key grouping near-duplicate variants, and
/// flags for likely duplicates, already-imported files, and likely-non-rules assets.
/// </summary>
/// <remarks>
/// Pure and side-effect free: it neither reads nor writes the database. Callers supply the set of
/// dedup keys already present in the library so the analyzer can flag re-imports.
/// </remarks>
public interface IScanCandidateAnalyzer
{
    /// <summary>
    /// Analyzes <paramref name="files"/> and returns one candidate per file, preserving input order.
    /// </summary>
    /// <param name="files">Files discovered in the folder.</param>
    /// <param name="existingDedupKeys">
    /// Dedup keys (see <see cref="ScanCandidateDto.DedupKey"/>) already represented in the library,
    /// compared case-insensitively. Used to set <see cref="ScanCandidateDto.AlreadyImported"/>.
    /// </param>
    /// <param name="fileContentHashes">
    /// Optional map of source path → SHA-256 content hash for the discovered files. When provided
    /// together with <paramref name="existingContentHashes"/>, files whose hash matches an
    /// already-indexed document are flagged <see cref="ScanCandidateFlag.ExactDuplicate"/>.
    /// </param>
    /// <param name="existingContentHashes">
    /// Content hashes already present in the library, compared case-insensitively. Used together
    /// with <paramref name="fileContentHashes"/> to detect exact (byte-for-byte) duplicates.
    /// </param>
    /// <param name="excludePatterns">
    /// Optional per-folder glob patterns (e.g. <c>*map*</c>, <c>*token*</c>). Files whose name
    /// matches any pattern are flagged <see cref="ScanCandidateFlag.LikelyNotRules"/>. When null,
    /// a built-in default signal list is used.
    /// </param>
    IReadOnlyList<ScanCandidateDto> Analyze(
        IReadOnlyList<DocumentSourceFile> files,
        IReadOnlySet<string> existingDedupKeys,
        IReadOnlyDictionary<string, string>? fileContentHashes = null,
        IReadOnlySet<string>? existingContentHashes = null,
        IReadOnlyList<string>? excludePatterns = null);
}
