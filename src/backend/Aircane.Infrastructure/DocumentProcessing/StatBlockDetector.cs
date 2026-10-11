using System.Globalization;
using System.Text.RegularExpressions;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>What a <see cref="TextSegment"/> contains.</summary>
public enum SegmentKind
{
    Prose,
    StatBlock,
}

/// <summary>
/// A run of the input text. <see cref="CreatureName"/> is set for stat blocks only. Concatenating the
/// <see cref="Text"/> of all segments reproduces the input exactly.
/// </summary>
public sealed record TextSegment(string Text, SegmentKind Kind, string? CreatureName = null);

/// <summary>
/// Finds monster / NPC stat blocks in extracted document text so they can be kept together as one chunk
/// instead of being cut by the sliding window (a split block loses part of its action list).
/// <para>
/// Detection is text-based only. A header is recognised in these shapes:
/// </para>
/// <list type="number">
///   <item>A name line followed by a size/type line: <c>Giant Crocodile</c> / <c>Huge beast, unaligned</c>.</item>
///   <item>A name and size/type on one line separated by a dash: <c>Giant Crocodile — Huge beast, unaligned</c>.</item>
///   <item>PF2e: a name line followed by <c>Creature 6</c>.</item>
///   <item>
///     Run-together text with no line breaks, as PdfPig and OCR often produce:
///     <c>GIANT. CROCODILE Huge beast, unaligned Armor Class 14 ...</c>. Because a name cannot be told
///     apart from the preceding prose here, this shape must be confirmed by <c>Armor Class</c> following
///     the size and type (or by <c>CREATURE N</c> for PF2e).
///   </item>
/// </list>
/// A size word alone is never enough: "Large coffee table" is not a header.
/// </summary>
public static class StatBlockDetector
{
    /// <summary>The longest stat block kept as one chunk before it is split at action boundaries.</summary>
    public const int MaxStatBlockChars = 4000;

    /// <summary>Do not cut a long stat block so early that the first part is trivially short.</summary>
    private const int MinPartChars = 200;

    private const string Size = "(?:Tiny|Small|Medium|Large|Huge|Gargantuan)";

    private const string CreatureType =
        "(?i:swarm|beast|humanoid|undead|dragon|fiend|celestial|construct|elemental|fey|giant|monstrosity|ooze|plant|aberration)";

    // One capitalised word of a run-together name. ALL-CAPS words may carry an OCR full stop or comma
    // ("GIANT."); other words may not, so the end of the previous sentence is not swallowed.
    private const string NameToken = @"(?:[A-Z][A-Z'’/\-]{2,}[.,]?|[A-Z][\w'’/\-]*)";

    // Name words chain across spaces only (never line breaks); lowercase connectors may sit between
    // capitalised words ("Gnoll Fang of Yeenoghu").
    private const string FlatBody =
        @"(?<name>" + NameToken + @"(?:[ \t]+(?:(?:of|the|and|de|la|von|van)[ \t]+)?" + NameToken + @"){0,5})" +
        @"[ \t\r\n]+(?:" + Size + @"[ \t\r\n]+" + CreatureType + @"\b[\s\S]{0,200}?\bArmor Class\b" +
        @"|CREATURE[ \t\r\n]+-?\d{1,2}\b|Creature[ \t\r\n]+-?\d{1,2}\b)";

    private static readonly Regex SizeTypeAtStart =
        new("^" + Size + @"\s+" + CreatureType + @"\b", RegexOptions.CultureInvariant);

    // "Name — Huge beast, unaligned" (em/en dash, or a spaced hyphen).
    private static readonly Regex NameDashSizeType =
        new(@"^(?<name>.{2,60}?)(?:\s[-—–]+\s|\s*[—–]+\s*)(?<rest>" + Size + @"\s+" + CreatureType + @"\b.*)$",
            RegexOptions.CultureInvariant);

    private static readonly Regex Pf2eLevelLine =
        new(@"^Creature\s+-?\d{1,2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FlatAnywhere = new(@"(?<![\w'’])" + FlatBody, RegexOptions.CultureInvariant);

    private static readonly Regex FlatAtLineStart = new("^" + FlatBody, RegexOptions.CultureInvariant);

    private static readonly Regex PageNumberToken = new(@"^[Il|]?\d+$", RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownHeading = new(@"^\s{0,3}#{1,6}\s+\S", RegexOptions.CultureInvariant);

    // "ACTIONS" anywhere (run-together text), or a Title-case label at the start of a line.
    private static readonly Regex ActionLabel = new(
        @"(?<![A-Za-z])(?:LEGENDARY ACTIONS|LAIR ACTIONS|REACTIONS|ACTIONS)(?![A-Za-z])" +
        @"|(?<=^|\n)[ \t]*(?:#{1,6}[ \t]*)?\*{0,2}(?:Legendary Actions|Lair Actions|Reactions|Actions)\b",
        RegexOptions.CultureInvariant);

    /// <summary>Section labels inside a stat block. They look like headings but do not end the block.</summary>
    private static readonly HashSet<string> StatBlockLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "actions", "reactions", "legendary actions", "lair actions", "bonus actions", "traits",
        "mythic actions", "villain actions", "regional effects",
    };

    private static readonly string[] StatLinePrefixes =
    [
        "armor class", "hit points", "speed", "challenge", "skills", "senses", "languages",
        "saving throws", "damage", "condition", "proficiency",
    ];

    private static readonly HashSet<string> LabelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTIONS", "REACTIONS", "LEGENDARY", "LAIR", "APPENDIX",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="line"/> (with <paramref name="nextLine"/> for the two-line forms) begins a
    /// stat block. Type words are matched case-insensitively; whitespace and simple markdown markup
    /// (<c>#</c>, <c>*</c>, <c>_</c>) around the lines are ignored.
    /// </summary>
    public static bool IsStatBlockHeader(string line, string? nextLine)
    {
        var current = StripMarkup(line);
        if (current.Length == 0)
            return false;

        if (NameDashSizeType.IsMatch(current) || FlatAtLineStart.IsMatch(current))
            return true;

        if (nextLine is null || !LooksLikeName(current))
            return false;

        var next = StripMarkup(nextLine);
        return SizeTypeAtStart.IsMatch(next) || Pf2eLevelLine.IsMatch(next);
    }

    /// <summary>
    /// Splits <paramref name="text"/> into stat-block and prose segments covering the whole input with no
    /// gaps or overlaps. A stat block runs from its header to the next stat-block header, a chapter or
    /// section heading (markdown, or an isolated title-case line), or the end of the text. Action labels
    /// ("Actions", "Reactions", ...) inside a block do not end it.
    /// </summary>
    public static IReadOnlyList<TextSegment> ExtractStatBlocks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var headers = FindHeaders(text);
        if (headers.Count == 0)
            return [new TextSegment(text, SegmentKind.Prose)];

        var headerStarts = headers.Select(h => h.Start).ToHashSet();
        var segments = new List<TextSegment>();
        var position = 0;

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (header.Start > position)
                segments.Add(new TextSegment(text[position..header.Start], SegmentKind.Prose));

            var limit = i + 1 < headers.Count ? headers[i + 1].Start : text.Length;
            var end = FindSectionBoundary(text, header.HeaderEnd, limit, headerStarts) ?? limit;

            segments.Add(new TextSegment(
                text[header.Start..end], SegmentKind.StatBlock, header.Name.Length > 0 ? header.Name : null));
            position = end;
        }

        if (position < text.Length)
            segments.Add(new TextSegment(text[position..], SegmentKind.Prose));

        return segments;
    }

    /// <summary>
    /// Splits a stat block longer than <paramref name="maxChars"/> into parts no longer than the limit,
    /// preferring the start of an action section ("ACTIONS", "Reactions", "Legendary Actions", "Lair
    /// Actions"), then a sentence end, then a hard cut. A block within the limit is returned as is.
    /// </summary>
    public static IReadOnlyList<string> SplitAtActionBoundaries(string statBlock, int maxChars = MaxStatBlockChars)
    {
        if (statBlock.Length <= maxChars)
            return [statBlock];

        var boundaries = ActionLabel.Matches(statBlock)
            .Select(m => m.Value.StartsWith('\n') ? m.Index + 1 : m.Index)
            .Where(i => i > 0)
            .OrderBy(i => i)
            .ToList();

        var parts = new List<string>();
        var start = 0;

        while (statBlock.Length - start > maxChars)
        {
            var windowEnd = start + maxChars;
            var cut = boundaries.LastOrDefault(b => b > start + MinPartChars && b <= windowEnd);

            if (cut == 0)
            {
                var sentenceEnd = statBlock.LastIndexOf(". ", windowEnd - 1, windowEnd - start, StringComparison.Ordinal);
                cut = sentenceEnd > start + MinPartChars ? sentenceEnd + 2 : windowEnd;
            }

            var part = statBlock[start..cut].Trim();
            if (part.Length > 0)
                parts.Add(part);
            start = cut;
        }

        var rest = statBlock[start..].Trim();
        if (rest.Length > 0)
            parts.Add(rest);

        return parts;
    }

    // ── Header discovery ──────────────────────────────────────────────────────

    private sealed record Header(int Start, int HeaderEnd, string Name);

    private static List<Header> FindHeaders(string text)
    {
        var candidates = new List<Header>();

        // Line-based forms.
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            var (start, line) = lines[i];
            var current = StripMarkup(line);
            if (current.Length == 0)
                continue;

            var dash = NameDashSizeType.Match(current);
            if (dash.Success)
            {
                candidates.Add(new Header(start, start + line.Length, CleanName(dash.Groups["name"].Value)));
                continue;
            }

            if (i + 1 < lines.Count && LooksLikeName(current))
            {
                var next = StripMarkup(lines[i + 1].Text);
                if (SizeTypeAtStart.IsMatch(next) || Pf2eLevelLine.IsMatch(next))
                    candidates.Add(new Header(start, lines[i + 1].Start + lines[i + 1].Text.Length, CleanName(current)));
            }
        }

        // Run-together text: name, size, type and "Armor Class" (or CREATURE N) on one line.
        foreach (Match match in FlatAnywhere.Matches(text))
        {
            // The name may come out empty when OCR left only a page number before the size/type; the
            // block is still a stat block, just unnamed.
            candidates.Add(new Header(
                match.Groups["name"].Index, match.Index + match.Length, CleanName(match.Groups["name"].Value)));
        }

        // Keep the earliest header of any overlapping group.
        var headers = new List<Header>();
        foreach (var candidate in candidates.OrderBy(c => c.Start).ThenByDescending(c => c.HeaderEnd))
        {
            if (headers.Count > 0 && candidate.Start < headers[^1].HeaderEnd)
                continue;
            headers.Add(candidate);
        }

        return headers;
    }

    /// <summary>
    /// The start of the first heading line in [<paramref name="from"/>, <paramref name="limit"/>) that ends
    /// the block: a markdown heading, or an isolated title-case line (blank line before and after).
    /// Creature headers, action labels and stat lines never count.
    /// </summary>
    private static int? FindSectionBoundary(string text, int from, int limit, HashSet<int> headerStarts)
    {
        var lines = SplitLines(text);

        for (var i = 0; i < lines.Count; i++)
        {
            var (start, line) = lines[i];
            if (start < from || start >= limit || headerStarts.Contains(start))
                continue;

            var stripped = StripMarkup(line);
            if (stripped.Length == 0 || IsStatBlockLabelOrStatLine(stripped))
                continue;

            if (MarkdownHeading.IsMatch(line))
                return start;

            var blankBefore = i > 0 && lines[i - 1].Text.Trim().Length == 0;
            var blankAfter = i + 1 < lines.Count && lines[i + 1].Text.Trim().Length == 0;
            if (blankBefore && blankAfter
                && SlidingWindowTextChunker.IsTitleCaseHeading(line, text, start + line.Length))
            {
                return start;
            }
        }

        return null;
    }

    private static bool IsStatBlockLabelOrStatLine(string stripped)
    {
        if (StatBlockLabels.Contains(stripped.TrimEnd(':')))
            return true;

        return StatLinePrefixes.Any(p => stripped.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<(int Start, string Text)> SplitLines(string text)
    {
        var lines = new List<(int, string)>();
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;

            var line = text[start..end];
            lines.Add((start, line.EndsWith('\r') ? line[..^1] : line));

            start = end + 1;
        }

        return lines;
    }

    /// <summary>Trims whitespace and simple markdown decoration (heading hashes, emphasis, quotes).</summary>
    private static string StripMarkup(string line) =>
        line.Trim().TrimStart('#', '>', ' ', '\t').Trim().Trim('*', '_').Trim();

    /// <summary>A plausible creature name line: short, not a sentence, and not itself a size/type line.</summary>
    private static bool LooksLikeName(string line)
    {
        if (line.Length is < 2 or > 60 || !line.Any(char.IsLetter))
            return false;
        if (line[^1] is '.' or ',' or ';' or ':' or '!' or '?')
            return false;
        if (SizeTypeAtStart.IsMatch(line))
            return false;

        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 7;
    }

    /// <summary>
    /// Tidies an extracted name: drops OCR punctuation and leading section labels, and title-cases
    /// ALL-CAPS names ("GIANT. CROCODILE" becomes "Giant Crocodile").
    /// </summary>
    private static string CleanName(string raw)
    {
        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', ':', ';', '*', '_', '#'))
            .Where(t => t.Length > 0 && !PageNumberToken.IsMatch(t)) // OCR'd page numbers like "I44"
            .SkipWhile(t => LabelWords.Contains(t))
            .ToList();

        var name = string.Join(' ', tokens);
        return name.Any(char.IsLower)
            ? name
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }
}
