using System.Text.RegularExpressions;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Library;

/// <summary>
/// Default <see cref="IScanCandidateAnalyzer"/> implementation. Pure heuristics over filenames;
/// see <see cref="FilenameNormalizer"/> for the shared title/dedup logic.
/// </summary>
public sealed partial class ScanCandidateAnalyzer : IScanCandidateAnalyzer
{
    /// <summary>
    /// Filename signals that indicate a non-rules asset (maps, screens, tokens, etc.). Intentionally
    /// small and conservative; the host can still choose to import a flagged file. Matched
    /// case-insensitively against the filename.
    /// </summary>
    [GeneratedRegex(
        @"(?:\bmaps?\b|\bmap pack\b|\bscreen\b|\btokens?\b|\bcounters?\b|\bcards?\b|\bvtt\b|\bposter\b|\bhandouts?\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotRulesSignalRegex();

    /// <inheritdoc />
    public IReadOnlyList<ScanCandidateDto> Analyze(
        IReadOnlyList<DocumentSourceFile> files,
        IReadOnlySet<string> existingDedupKeys)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(existingDedupKeys);

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

            // Duplicate variant within this batch (e.g. BnW OCR vs Color OCR of the same book).
            if (!string.IsNullOrEmpty(key) && keyCounts.GetValueOrDefault(key) > 1)
            {
                flags.Add(ScanCandidateFlag.DuplicateVariant);
                reasons.Add("Looks like another copy/variant of the same title in this folder.");
            }

            // Already present in the library.
            var alreadyImported = !string.IsNullOrEmpty(key) && existingDedupKeys.Contains(key);
            if (alreadyImported)
            {
                flags.Add(ScanCandidateFlag.AlreadyImported);
                reasons.Add("A matching document is already in your library.");
            }

            // Likely a non-rules asset (map pack, screen, tokens…).
            if (NotRulesSignalRegex().IsMatch(file.FileName))
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
                AlreadyImported: alreadyImported));
        }

        return candidates;
    }
}
