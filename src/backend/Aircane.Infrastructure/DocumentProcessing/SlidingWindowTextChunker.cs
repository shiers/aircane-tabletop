using System.Text.RegularExpressions;
using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// Splits text into overlapping chunks using a sliding-window strategy that prefers structural
/// boundaries. For each chunk the window's tail is searched, in order, for:
/// <list type="number">
///   <item>a heading (markdown or title-case), within <see cref="HeadingLookBack"/> characters;</item>
///   <item>a paragraph break (blank line), within <see cref="ParagraphLookBack"/> characters;</item>
///   <item>a newline or sentence end, within <see cref="BoundaryLookBack"/> characters;</item>
///   <item>otherwise a hard cut at the maximum chunk size.</item>
/// </list>
/// A split at a heading puts the heading at the START of the next chunk, so the chunk holding a
/// section's content also holds its heading. No overlap is carried back across that boundary,
/// because overlap would prepend the previous section's text in front of the heading. All other
/// splits overlap by <see cref="OverlapSize"/> characters.
/// </summary>
/// <remarks>
/// Each chunk's <see cref="TextChunk.SectionTitle"/> is the heading it starts with or, failing
/// that, the most recent heading before it (falling back to the caller-supplied title).
/// <para>
/// Documents imported before the 1100/300 defaults were introduced were chunked with 2000/200 and
/// no heading awareness. They keep their old chunks until they are re-imported or re-indexed;
/// that is a manual operator step and is never triggered automatically.
/// </para>
/// </remarks>
public sealed partial class SlidingWindowTextChunker : ITextChunker
{
    /// <summary>Default maximum characters per chunk.</summary>
    public const int DefaultMaxChunkSize = 1100;

    /// <summary>Default characters shared between consecutive chunks (about 27% of the chunk size).</summary>
    public const int DefaultOverlapSize = 300;

    /// <summary>How far back from the hard cut a heading is preferred as the split point.</summary>
    private const int HeadingLookBack = 400;

    /// <summary>How far back from the hard cut a paragraph break is preferred as the split point.</summary>
    private const int ParagraphLookBack = 300;

    /// <summary>How far back from the hard cut to look for a newline or sentence end.</summary>
    private const int BoundaryLookBack = 200;

    private const int TitleCaseMinLength = 3;
    private const int TitleCaseMaxLength = 60;

    /// <summary>Short words allowed to stay lower-case in a title-case heading ("Armor of Invulnerability").</summary>
    private static readonly HashSet<string> MinorWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "by", "for", "in", "of", "on", "or", "the", "to", "with",
    };

    /// <summary>Maximum number of characters per chunk.</summary>
    public int MaxChunkSize { get; }

    /// <summary>Number of characters to overlap between consecutive chunks.</summary>
    public int OverlapSize { get; }

    public SlidingWindowTextChunker(int maxChunkSize = DefaultMaxChunkSize, int overlapSize = DefaultOverlapSize)
    {
        if (maxChunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChunkSize), "Must be greater than zero.");
        if (overlapSize < 0)
            throw new ArgumentOutOfRangeException(nameof(overlapSize), "Must be zero or greater.");
        if (overlapSize >= maxChunkSize)
            throw new ArgumentOutOfRangeException(nameof(overlapSize), "Must be less than maxChunkSize.");

        MaxChunkSize = maxChunkSize;
        OverlapSize = overlapSize;
    }

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> Chunk(
        string text,
        int pageNumber,
        int startingChunkIndex = 0,
        string? sectionTitle = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TextChunk>();

        // Normalize line endings so every boundary search only deals with '\n'.
        text = text.Replace("\r\n", "\n");

        var headings = FindHeadings(text);
        var chunks = new List<TextChunk>();
        int position = 0;
        int chunkIndex = startingChunkIndex;

        while (position < text.Length)
        {
            int remaining = text.Length - position;

            if (remaining <= MaxChunkSize)
            {
                // Last (or only) chunk - take everything that's left.
                AddChunk(chunks, text, position, text.Length, headings, pageNumber, sectionTitle, ref chunkIndex);
                break;
            }

            int hardCut = position + MaxChunkSize;
            var (splitAt, atHeading) = FindSplit(text, headings, position, hardCut);

            AddChunk(chunks, text, position, splitAt, headings, pageNumber, sectionTitle, ref chunkIndex);

            // A heading split starts the next chunk exactly at the heading. Any other split steps
            // back by the overlap amount so consecutive chunks share context.
            int nextPosition = atHeading ? splitAt : splitAt - OverlapSize;

            // Guard against infinite loops: always advance by at least one character.
            if (nextPosition <= position)
                nextPosition = position + 1;

            position = nextPosition;
        }

        return chunks.AsReadOnly();
    }

    private static void AddChunk(
        List<TextChunk> chunks,
        string text,
        int start,
        int end,
        IReadOnlyList<Heading> headings,
        int pageNumber,
        string? fallbackTitle,
        ref int chunkIndex)
    {
        var chunkText = text[start..end].Trim();
        if (chunkText.Length == 0)
            return;

        var title = ResolveSectionTitle(text, headings, start, fallbackTitle);
        chunks.Add(new TextChunk(chunkText, pageNumber, title, chunkIndex++));
    }

    /// <summary>
    /// The heading the chunk starts with, else the most recent heading that begins before the chunk,
    /// else <paramref name="fallbackTitle"/>.
    /// </summary>
    private static string? ResolveSectionTitle(
        string text,
        IReadOnlyList<Heading> headings,
        int chunkStart,
        string? fallbackTitle)
    {
        // First non-whitespace character of the chunk.
        int contentStart = chunkStart;
        while (contentStart < text.Length && char.IsWhiteSpace(text[contentStart]))
            contentStart++;

        string? mostRecent = null;
        foreach (var heading in headings)
        {
            if (heading.ContentStart == contentStart)
                return heading.Title; // chunk begins with this heading

            if (heading.LineStart < contentStart)
                mostRecent = heading.Title;
            else
                break; // headings are in document order
        }

        return mostRecent ?? fallbackTitle;
    }

    /// <summary>
    /// Chooses where the chunk starting at <paramref name="position"/> ends, searching backwards from
    /// <paramref name="hardCut"/>. <c>atHeading</c> is true when the split is at the start of a heading line.
    /// </summary>
    private (int SplitAt, bool AtHeading) FindSplit(
        string text,
        IReadOnlyList<Heading> headings,
        int position,
        int hardCut)
    {
        // 1. Heading within the last HeadingLookBack characters (latest one wins). A heading at the
        //    chunk's own start is skipped: splitting there would produce an empty chunk.
        int headingWindowStart = Math.Max(position + 1, hardCut - HeadingLookBack);
        for (int i = headings.Count - 1; i >= 0; i--)
        {
            var lineStart = headings[i].LineStart;
            if (lineStart >= hardCut)
                continue;
            if (lineStart < headingWindowStart)
                break;
            return (lineStart, true);
        }

        // 2. Paragraph break (blank line) within the last ParagraphLookBack characters.
        int paragraphStart = Math.Max(position, hardCut - ParagraphLookBack);
        int paragraphBreak = text.LastIndexOf("\n\n", hardCut - 1, hardCut - paragraphStart, StringComparison.Ordinal);
        if (paragraphBreak >= paragraphStart)
            return (paragraphBreak + 2, false); // position after the double newline

        // 3. Newline or sentence end within the last BoundaryLookBack characters.
        int searchStart = Math.Max(position, hardCut - BoundaryLookBack);
        for (int i = hardCut - 1; i >= searchStart; i--)
        {
            if (text[i] is '\n' or '.')
                return (i + 1, false); // position after the split character
        }

        // 4. No boundary found - use the hard cut.
        return (hardCut, false);
    }

    // ── Heading detection ─────────────────────────────────────────────────────

    /// <summary>A heading line in the text.</summary>
    /// <param name="LineStart">Offset of the first character of the line.</param>
    /// <param name="ContentStart">Offset of the first non-whitespace character of the line.</param>
    /// <param name="Title">The heading text, without markdown hashes or surrounding whitespace.</param>
    private sealed record Heading(int LineStart, int ContentStart, string Title);

    [GeneratedRegex(@"^\s*#{1,4}\s+\S")]
    private static partial Regex MarkdownHeadingPattern();

    [GeneratedRegex(@"^\s*#{1,4}\s+")]
    private static partial Regex MarkdownHashPrefix();

    /// <summary>Finds every heading line, in document order.</summary>
    private static List<Heading> FindHeadings(string text)
    {
        var headings = new List<Heading>();
        int lineStart = 0;

        while (lineStart < text.Length)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = text.Length;

            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            var lineText = line.ToString();

            string? title = null;
            if (MarkdownHeadingPattern().IsMatch(lineText))
                title = MarkdownHashPrefix().Replace(lineText, string.Empty).Trim();
            else if (IsTitleCaseHeading(lineText, text, lineEnd))
                title = lineText.Trim();

            if (!string.IsNullOrEmpty(title))
            {
                int contentStart = lineStart;
                while (contentStart < lineEnd && char.IsWhiteSpace(text[contentStart]))
                    contentStart++;
                headings.Add(new Heading(lineStart, contentStart, title));
            }

            lineStart = lineEnd + 1;
        }

        return headings;
    }

    /// <summary>
    /// A title-case heading is a line of 3-60 characters in which every word is capitalised (short
    /// connector words such as "of" may stay lower-case), with no trailing sentence punctuation,
    /// followed by a blank line or a line starting with a lower-case letter.
    /// </summary>
    private static bool IsTitleCaseHeading(string line, string text, int lineEnd)
    {
        var trimmed = line.Trim();
        if (trimmed.Length is < TitleCaseMinLength or > TitleCaseMaxLength)
            return false;

        if (trimmed[^1] is '.' or ',' or ';' or ':' or '!' or '?')
            return false;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            bool capitalised = char.IsUpper(word[0]) || char.IsDigit(word[0]);
            bool minor = i > 0 && MinorWords.Contains(word);
            if (!capitalised && !minor)
                return false;
        }

        // Must contain at least one letter (excludes lines of digits or symbols).
        if (!trimmed.Any(char.IsLetter))
            return false;

        // What follows: a blank line, or a line starting with a lower-case letter.
        int next = lineEnd + 1;
        if (next >= text.Length)
            return false;

        int nextEnd = text.IndexOf('\n', next);
        if (nextEnd < 0)
            nextEnd = text.Length;

        var nextLine = text.AsSpan(next, nextEnd - next).Trim();
        return nextLine.Length == 0 || char.IsLower(nextLine[0]);
    }
}
