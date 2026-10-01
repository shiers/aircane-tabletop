using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// Splits text into overlapping chunks using a sliding-window strategy.
/// Prefers splitting at paragraph or sentence boundaries when they fall within
/// the configured look-back window.
/// </summary>
public sealed class SlidingWindowTextChunker : ITextChunker
{
    /// <summary>Maximum number of characters per chunk (default 2000).</summary>
    public int MaxChunkSize { get; }

    /// <summary>Number of characters to overlap between consecutive chunks (default 200).</summary>
    public int OverlapSize { get; }

    /// <summary>
    /// Characters that are considered good split points, in preference order.
    /// A double-newline (paragraph break) is tried first, then single newline, then period.
    /// </summary>
    private static readonly char[] SplitChars = ['\n', '.'];

    /// <summary>
    /// How far back from the hard cut-off to search for a natural split point.
    /// </summary>
    private const int BoundaryLookBack = 200;

    public SlidingWindowTextChunker(int maxChunkSize = 2000, int overlapSize = 200)
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

        var chunks = new List<TextChunk>();
        int position = 0;
        int chunkIndex = startingChunkIndex;

        while (position < text.Length)
        {
            int remaining = text.Length - position;

            if (remaining <= MaxChunkSize)
            {
                // Last (or only) chunk - take everything that's left.
                var lastText = text[position..].Trim();
                if (lastText.Length > 0)
                {
                    chunks.Add(new TextChunk(lastText, pageNumber, sectionTitle, chunkIndex++));
                }
                break;
            }

            // Find a natural boundary within the look-back window before the hard cut.
            int hardCut = position + MaxChunkSize;
            int splitAt = FindNaturalBoundary(text, hardCut);

            var chunkText = text[position..splitAt].Trim();
            if (chunkText.Length > 0)
            {
                chunks.Add(new TextChunk(chunkText, pageNumber, sectionTitle, chunkIndex++));
            }

            // Advance position, stepping back by the overlap amount so consecutive
            // chunks share context.
            int nextPosition = splitAt - OverlapSize;

            // Guard against infinite loops: always advance by at least one character.
            if (nextPosition <= position)
                nextPosition = position + 1;

            position = nextPosition;
        }

        return chunks.AsReadOnly();
    }

    /// <summary>
    /// Searches backwards from <paramref name="hardCut"/> for a paragraph break,
    /// newline, or sentence-ending period. Returns <paramref name="hardCut"/> when
    /// no suitable boundary is found within <see cref="BoundaryLookBack"/> characters.
    /// </summary>
    private static int FindNaturalBoundary(string text, int hardCut)
    {
        // Prefer paragraph break (double newline) first.
        int searchStart = Math.Max(0, hardCut - BoundaryLookBack);
        int searchLength = hardCut - searchStart;

        // Look for "\n\n" (paragraph break).
        int paraBreak = text.LastIndexOf("\n\n", hardCut - 1, searchLength, StringComparison.Ordinal);
        if (paraBreak >= searchStart)
            return paraBreak + 2; // position after the double newline

        // Fall back to any split character.
        for (int i = hardCut - 1; i >= searchStart; i--)
        {
            if (Array.IndexOf(SplitChars, text[i]) >= 0)
                return i + 1; // position after the split character
        }

        // No boundary found - use the hard cut.
        return hardCut;
    }
}
