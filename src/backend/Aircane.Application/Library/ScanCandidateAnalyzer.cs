using System.Text.RegularExpressions;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Library;

/// <summary>
/// Default <see cref="IScanCandidateAnalyzer"/> implementation. Pure heuristics over filenames
/// (plus optional content-hash and exclude-pattern inputs supplied by the caller); see
/// <see cref="FilenameNormalizer"/> for the shared title/dedup logic.
/// </summary>
public sealed partial class ScanCandidateAnalyzer : IScanCandidateAnalyzer
{
    /// <summary>
    /// Default filename signals that indicate a non-rules asset (maps, screens, tokens, etc.).
    /// Used when the caller does not supply per-folder exclude patterns. Intentionally small and
    /// conservative; the host can still choose to import a flagged file.
    /// </summary>
    [GeneratedRegex(
        @"(?:\bmaps?\b|\bmap pack\b|\bscreen\b|\btokens?\b|\bcounters?\b|\bcards?\b|\bvtt\b|\bposter\b|\bhandouts?\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotRulesSignalRegex();

    /// <inheritdoc />
    public IReadOnlyList<ScanCandidateDto> Analyze(
        IReadOnlyList<DocumentSourceFile> files,
        IReadOnlySet<string> existingDedupKeys,
        IReadOnlyDictionary<string, string>? fileContentHashes = null,
        IReadOnlySet<string>? existingContentHashes = null,
        IReadOnlyList<string>? excludePatterns = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(existingDedupKeys);

        // Compile any caller-supplied exclude globs to regexes once.
        var excludeRegexes = CompileExcludePatterns(excludePatterns);

        // First pass: compute dedup keys so we can detect within-batch duplicate groups.
        var keyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var keys = new string[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var key = FilenameNormalizer.ComputeDedupKey(files[i].FileName);
            keys[i] = key;
            if (!string.IsNullOrEmpty(key))
                keyCounts[key] = keyCounts.GetValueOrDefault(key) + 1;
        }

        var candidates = new List<ScanCandidateDto>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var key = keys[i];

            var flags = new List<ScanCandidateFlag>();
            var reasons = new List<string>();

            // Exact (byte-for-byte) duplicate of an already-indexed document.
            var isExactDuplicate = false;
            if (fileContentHashes is not null && existingContentHashes is not null &&
                fileContentHashes.TryGetValue(file.SourcePath, out var hash) &&
                !string.IsNullOrEmpty(hash) && existingContentHashes.Contains(hash))
            {
                isExactDuplicate = true;
                flags.Add(ScanCandidateFlag.ExactDuplicate);
                reasons.Add("This file's contents are identical to a document already in your library.");
            }

            // Duplicate variant within this batch (e.g. BnW OCR vs Color OCR of the same book).
            if (!string.IsNullOrEmpty(key) && keyCounts.GetValueOrDefault(key) > 1)
            {
                flags.Add(ScanCandidateFlag.DuplicateVariant);
                reasons.Add("Looks like another copy/variant of the same title in this folder.");
            }

            // Already present in the library (by normalized filename identity). Redundant to show
            // alongside an ExactDuplicate flag, so only add when not already an exact duplicate.
            var alreadyImported = !string.IsNullOrEmpty(key) && existingDedupKeys.Contains(key);
            if (alreadyImported && !isExactDuplicate)
            {
                flags.Add(ScanCandidateFlag.AlreadyImported);
                reasons.Add("A matching document is already in your library.");
            }

            // Likely a non-rules asset (map pack, screen, tokens…), per exclude patterns or defaults.
            if (MatchesNotRules(file.FileName, excludeRegexes))
            {
                flags.Add(ScanCandidateFlag.LikelyNotRules);
                reasons.Add("Filename suggests a map/asset pack rather than rules text.");
            }

            candidates.Add(new ScanCandidateDto(
                SourcePath: file.SourcePath,
                FileName: file.FileName,
                SuggestedTitle: FilenameNormalizer.SuggestTitle(file.FileName),
                SuggestedRuleset: FilenameNormalizer.DetectRulesetYear(file.FileName),
                SizeBytes: file.SizeBytes,
                DedupKey: key,
                Flags: flags,
                Reason: reasons.Count > 0 ? string.Join(" ", reasons) : null,
                AlreadyImported: alreadyImported || isExactDuplicate));
        }

        return candidates;
    }

    private static bool MatchesNotRules(string fileName, IReadOnlyList<Regex>? excludeRegexes)
    {
        if (excludeRegexes is { Count: > 0 })
            return excludeRegexes.Any(r => r.IsMatch(fileName));

        return NotRulesSignalRegex().IsMatch(fileName);
    }

    private static List<Regex>? CompileExcludePatterns(IReadOnlyList<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
            return null;

        var result = new List<Regex>(patterns.Count);
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            result.Add(GlobToRegex(pattern));
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Converts a simple glob pattern (<c>*</c> = any run, <c>?</c> = single char) into a
    /// case-insensitive regex matched anywhere in the filename.
    /// </summary>
    internal static Regex GlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob.Trim())
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return new Regex(escaped, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
