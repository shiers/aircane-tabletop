using System.Text.RegularExpressions;

namespace Aircane.Application.Library;

/// <summary>
/// Best-effort filename heuristics used by the folder-scan review flow to suggest a clean
/// document title, detect an edition/ruleset year, and compute a "dedup key" that groups
/// near-duplicate scans of the same work (e.g. "... (BnW OCR)" vs "... (Color OCR)").
/// </summary>
/// <remarks>
/// This mirrors the frontend <c>filenameSuggestions.ts</c> logic. For the folder-scan flow the
/// backend is the source of truth: the analyzer runs here and the frontend renders the result.
/// Nothing here is authoritative — these are hints the host can override.
/// </remarks>
public static partial class FilenameNormalizer
{
    // Bracket group whose body contains no bracket chars. Excluding the opening brackets from the
    // body keeps this linear (no catastrophic backtracking).
    [GeneratedRegex(@"[([{][^()[\]{}]*[)\]}]", RegexOptions.None)]
    private static partial Regex QualifierRegex();

    [GeneratedRegex(@"_+", RegexOptions.None)]
    private static partial Regex UnderscoreRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex WhitespaceRegex();

    // "DnD", "D&D", "D and D", optionally followed by an edition like "5e".
    [GeneratedRegex(@"\bd\s*(?:&|n|and)\s*d(?:\s*(\d+e))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DndRegex();

    // "Pf2e" / "PF 2e".
    [GeneratedRegex(@"\bpf\s*(\d+e)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PathfinderRegex();

    // Standalone 19xx/20xx year not glued to other digits.
    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.None)]
    private static partial Regex YearRegex();

    // Bare-word edition/variant qualifiers that describe a printing rather than the work itself.
    // Stripped for DEDUP ONLY so "Tome of Foes Deluxe" groups with "Tome of Foes"; the suggested
    // title keeps the word. Kept conservative and multi-word aware (e.g. "special edition").
    [GeneratedRegex(
        @"\b(?:deluxe|revised|reprint|reprinted|anniversary|special\s+edition|limited\s+edition|collector'?s\s+edition|premium|hardcover|softcover|digital|remastered|scan|scanned|ocr|hi-?res|high\s+res|low\s+res|compressed|bookmarked|searchable)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex VariantQualifierRegex();

    /// <summary>
    /// Suggests a human-readable document title from a filename.
    /// Example: "DnD 5e Dungeon Masters Guide (Color OCR).pdf" → "D&amp;D 5e Dungeon Masters Guide".
    /// Returns an empty string for null/empty/whitespace input.
    /// </summary>
    public static string SuggestTitle(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var withoutExt = StripExtension(fileName);
        var withoutQualifiers = QualifierRegex().Replace(withoutExt, " ").Trim();
        var tidied = TidySeparators(withoutQualifiers);
        return ExpandAbbreviations(tidied);
    }

    /// <summary>
    /// Extracts a plausible edition/ruleset year (standalone 19xx/20xx) from a filename, or null
    /// when none is present. Never infers an edition that isn't written in the name.
    /// </summary>
    public static string? DetectRulesetYear(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var match = YearRegex().Match(fileName);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Computes a normalized key used to group near-duplicate files (different scans/editions of
    /// the same work). Strips extension, qualifiers, and edition years, then lowercases and
    /// collapses whitespace. Two files that differ only by "(BnW OCR)" vs "(Color OCR)" produce
    /// the same key.
    /// </summary>
    public static string ComputeDedupKey(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var withoutExt = StripExtension(fileName);
        var withoutQualifiers = QualifierRegex().Replace(withoutExt, " ");
        // Drop standalone edition years for dedup purposes so that different scans of the same
        // work group together regardless of an embedded year.
        var withoutYear = YearRegex().Replace(withoutQualifiers, " ");
        // Drop bare-word variant qualifiers (Deluxe, Revised, OCR, …) so different printings/scans
        // of the same work group together even when the qualifier isn't bracketed.
        var withoutVariants = VariantQualifierRegex().Replace(withoutYear, " ");
        var tidied = TidySeparators(withoutVariants);
        return WhitespaceRegex().Replace(tidied, " ").Trim().ToLowerInvariant();
    }

    private static string StripExtension(string fileName)
    {
        var idx = fileName.LastIndexOf('.');
        return idx > 0 ? fileName[..idx] : fileName;
    }

    private static string TidySeparators(string value)
    {
        var collapsed = WhitespaceRegex()
            .Replace(UnderscoreRegex().Replace(value, " "), " ")
            .Trim();
        return TrimChar(collapsed, '-').Trim();
    }

    /// <summary>Removes leading/trailing runs of a single delimiter char (linear, no regex).</summary>
    private static string TrimChar(string value, char ch)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && value[start] == ch) start++;
        while (end > start && value[end - 1] == ch) end--;
        return value[start..end];
    }

    private static string ExpandAbbreviations(string value)
    {
        var result = DndRegex().Replace(value, m =>
            m.Groups[1].Success ? $"D&D {m.Groups[1].Value.ToLowerInvariant()}" : "D&D");
        result = PathfinderRegex().Replace(result, m =>
            $"Pathfinder {m.Groups[1].Value.ToLowerInvariant()}");
        return WhitespaceRegex().Replace(result, " ").Trim();
    }
}
