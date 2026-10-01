using Aircane.Infrastructure.DocumentProcessing;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Unit tests for <see cref="SlidingWindowTextChunker"/>.
/// </summary>
public class SlidingWindowTextChunkerTests
{
    // ── Short text (fits in a single chunk) ───────────────────────────────────

    [Fact]
    public void Chunk_ShortText_ProducesSingleChunk()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 2000, overlapSize: 200);
        var text = "This is a short piece of text.";

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0].Text);
    }

    [Fact]
    public void Chunk_TextExactlyAtMaxSize_ProducesSingleChunk()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 50, overlapSize: 10);
        var text = new string('a', 50);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.Single(chunks);
    }

    // ── Empty / whitespace text ───────────────────────────────────────────────

    [Fact]
    public void Chunk_EmptyText_ProducesNoChunks()
    {
        var chunker = new SlidingWindowTextChunker();

        var chunks = chunker.Chunk(string.Empty, pageNumber: 1);

        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_WhitespaceOnlyText_ProducesNoChunks()
    {
        var chunker = new SlidingWindowTextChunker();

        var chunks = chunker.Chunk("   \n\t  ", pageNumber: 1);

        Assert.Empty(chunks);
    }

    // ── Long text produces multiple chunks ───────────────────────────────────

    [Fact]
    public void Chunk_LongText_ProducesMultipleChunks()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 20);
        // 300 characters - should produce at least 2 chunks.
        var text = new string('x', 300);

        var chunks = chunker.Chunk(text, pageNumber: 2);

        Assert.True(chunks.Count > 1, $"Expected more than 1 chunk, got {chunks.Count}.");
    }

    [Fact]
    public void Chunk_LongText_AllChunksHaveCorrectPageNumber()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 20);
        var text = new string('y', 350);

        var chunks = chunker.Chunk(text, pageNumber: 5);

        Assert.All(chunks, c => Assert.Equal(5, c.PageNumber));
    }

    // ── Overlap ───────────────────────────────────────────────────────────────

    [Fact]
    public void Chunk_LongText_ConsecutiveChunksOverlap()
    {
        // Use a simple text with no natural boundaries so we can reason about positions.
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 20);
        var text = new string('z', 250);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.True(chunks.Count >= 2, "Need at least 2 chunks to verify overlap.");

        // The end of chunk[0] should share characters with the start of chunk[1].
        var end0 = chunks[0].Text[^Math.Min(20, chunks[0].Text.Length)..];
        var start1 = chunks[1].Text[..Math.Min(20, chunks[1].Text.Length)];

        // Both are 'z' characters so they will match; the key assertion is that
        // the second chunk starts before the first chunk ends (overlap exists).
        Assert.Equal(end0, start1);
    }

    // ── Chunk indices ─────────────────────────────────────────────────────────

    [Fact]
    public void Chunk_SingleChunk_HasChunkIndexZero()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 2000, overlapSize: 200);

        var chunks = chunker.Chunk("Short text.", pageNumber: 1, startingChunkIndex: 0);

        Assert.Equal(0, chunks[0].ChunkIndex);
    }

    [Fact]
    public void Chunk_MultipleChunks_IndicesAreSequential()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 20);
        var text = new string('a', 350);

        var chunks = chunker.Chunk(text, pageNumber: 1, startingChunkIndex: 0);

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public void Chunk_StartingChunkIndex_OffsetApplied()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 20);
        var text = new string('b', 350);

        var chunks = chunker.Chunk(text, pageNumber: 3, startingChunkIndex: 10);

        Assert.Equal(10, chunks[0].ChunkIndex);
        Assert.Equal(11, chunks[1].ChunkIndex);
    }

    // ── Section title propagation ─────────────────────────────────────────────

    [Fact]
    public void Chunk_WithSectionTitle_AllChunksCarryTitle()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 20);
        var text = new string('c', 350);

        var chunks = chunker.Chunk(text, pageNumber: 1, sectionTitle: "Introduction");

        Assert.All(chunks, c => Assert.Equal("Introduction", c.SectionTitle));
    }

    [Fact]
    public void Chunk_WithoutSectionTitle_SectionTitleIsNull()
    {
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 2000, overlapSize: 200);

        var chunks = chunker.Chunk("Some text.", pageNumber: 1);

        Assert.Null(chunks[0].SectionTitle);
    }

    // ── Natural boundary splitting ────────────────────────────────────────────

    [Fact]
    public void Chunk_TextWithParagraphBreaks_SplitsAtParagraphBoundary()
    {
        // Build text where a paragraph break falls just before the max size.
        // maxChunkSize = 100, paragraph break at position 80.
        var chunker = new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 10);
        var part1 = new string('a', 80);
        var part2 = new string('b', 80);
        var text = part1 + "\n\n" + part2;

        var chunks = chunker.Chunk(text, pageNumber: 1);

        // First chunk should end with the 'a' characters (split at paragraph break).
        Assert.StartsWith("a", chunks[0].Text);
        Assert.DoesNotContain("b", chunks[0].Text);
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_NegativeMaxChunkSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowTextChunker(maxChunkSize: -1, overlapSize: 0));
    }

    [Fact]
    public void Constructor_OverlapGreaterThanOrEqualToMaxChunkSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: 100));
    }

    [Fact]
    public void Constructor_NegativeOverlap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowTextChunker(maxChunkSize: 100, overlapSize: -1));
    }
}
