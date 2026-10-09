using System.Text;
using Aircane.Infrastructure.DocumentProcessing;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Tests for the 1100/300 defaults, heading-aware splitting and section title propagation in
/// <see cref="SlidingWindowTextChunker"/>.
/// </summary>
public class SlidingWindowTextChunkerHeadingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sentences totalling exactly <paramref name="length"/> characters, ending with a period.</summary>
    private static string Sentences(string label, int length)
    {
        var sb = new StringBuilder();
        while (sb.Length < length)
            sb.Append($"The {label} sentence says something. ");
        return sb.ToString(0, length - 1).TrimEnd() + ".";
    }

    /// <summary>Distinct space-separated tokens with no sentence or line boundaries, so overlap can be measured.</summary>
    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"w{i:D5}"));

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_Are1100ChunkSizeAnd300Overlap()
    {
        var chunker = new SlidingWindowTextChunker();

        Assert.Equal(1100, chunker.MaxChunkSize);
        Assert.Equal(300, chunker.OverlapSize);
    }

    [Fact]
    public void Chunk_DefaultSizes_NoChunkExceedsMaxSize()
    {
        var chunker = new SlidingWindowTextChunker();

        var chunks = chunker.Chunk(Words(1200), pageNumber: 1);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 1100));
    }

    // ── Heading-aware splitting ───────────────────────────────────────────────

    [Fact]
    public void Chunk_MarkdownHeadingMidWindow_SplitsAtHeadingNotAtSentence()
    {
        var chunker = new SlidingWindowTextChunker();
        // Heading at ~800 of a 1100 window. The second section has sentence ends, a newline and a
        // paragraph break closer to the hard cut, which a heading split must still beat.
        var section2 = Sentences("second", 200) + "\n\n" + Sentences("second", 400);
        var text = Sentences("first", 800) + "\n## Grappling\n" + section2;

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.True(chunks.Count >= 2);
        Assert.DoesNotContain("Grappling", chunks[0].Text);
        Assert.Contains("first sentence", chunks[0].Text);
        Assert.DoesNotContain("second sentence", chunks[0].Text);
        Assert.StartsWith("## Grappling", chunks[1].Text);
    }

    [Fact]
    public void Chunk_TitleCaseHeadingBeforeBlankLine_SplitsAtHeading()
    {
        var chunker = new SlidingWindowTextChunker();
        var text = Sentences("first", 800)
            + "\nGrappling Rules\n\n"
            + Sentences("second", 600);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.True(chunks.Count >= 2);
        Assert.DoesNotContain("Grappling Rules", chunks[0].Text);
        Assert.StartsWith("Grappling Rules", chunks[1].Text);
        Assert.Equal("Grappling Rules", chunks[1].SectionTitle);
    }

    [Fact]
    public void Chunk_TitleCaseHeadingBeforeLowercaseLine_IsAHeading()
    {
        var chunker = new SlidingWindowTextChunker();
        var text = Sentences("first", 800)
            + "\nArmor of Invulnerability\nyou gain resistance to nonmagical damage. "
            + Sentences("second", 500);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.StartsWith("Armor of Invulnerability", chunks[1].Text);
        Assert.Equal("Armor of Invulnerability", chunks[1].SectionTitle);
    }

    [Theory]
    [InlineData("This Is A Sentence.")]   // trailing punctuation
    [InlineData("not a heading at all")]  // lower-case words
    [InlineData("Ab")]                    // too short
    public void Chunk_NonHeadingLines_DoNotCreateSectionTitles(string line)
    {
        var chunker = new SlidingWindowTextChunker();
        var text = Sentences("first", 300) + "\n" + line + "\n\n" + Sentences("second", 300);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.All(chunks, c => Assert.Null(c.SectionTitle));
    }

    [Fact]
    public void Chunk_TitleCaseLineFollowedByCapitalisedText_IsNotAHeading()
    {
        var chunker = new SlidingWindowTextChunker();
        var text = Sentences("first", 300) + "\nSome Name\nNext Line Starts Upper. " + Sentences("second", 300);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.All(chunks, c => Assert.Null(c.SectionTitle));
    }

    [Fact]
    public void Chunk_CrLfLineEndings_AreHandled()
    {
        var chunker = new SlidingWindowTextChunker();
        var text = (Sentences("first", 800) + "\n## Grappling\n" + Sentences("second", 600)).Replace("\n", "\r\n");

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.StartsWith("## Grappling", chunks[1].Text);
    }

    // ── Section title propagation ─────────────────────────────────────────────

    [Fact]
    public void Chunk_StartingWithHeading_UsesHeadingAsSectionTitle()
    {
        var chunker = new SlidingWindowTextChunker();
        var text = "## Opportunity Attacks\n" + Sentences("body", 300);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.Equal("Opportunity Attacks", Assert.Single(chunks).SectionTitle);
    }

    [Fact]
    public void Chunk_NotStartingWithHeading_CarriesMostRecentHeading()
    {
        var chunker = new SlidingWindowTextChunker();
        // One heading followed by a long unstructured section that needs several chunks.
        var text = "# Exploration\n" + Words(900);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.True(chunks.Count >= 3);
        Assert.StartsWith("# Exploration", chunks[0].Text);
        Assert.All(chunks, c => Assert.Equal("Exploration", c.SectionTitle));
    }

    [Fact]
    public void Chunk_SectionTitleParameter_IsFallbackOnly()
    {
        var chunker = new SlidingWindowTextChunker();

        var withoutHeading = chunker.Chunk(Sentences("plain", 300), pageNumber: 1, sectionTitle: "Carried");
        var withHeading = chunker.Chunk("## Real Heading\n" + Sentences("plain", 300), pageNumber: 1, sectionTitle: "Carried");

        Assert.Equal("Carried", withoutHeading[0].SectionTitle);
        Assert.Equal("Real Heading", withHeading[0].SectionTitle);
    }

    [Fact]
    public void Chunk_MarkdownHeadingLevels_StripHashes()
    {
        var chunker = new SlidingWindowTextChunker();

        foreach (var (prefix, expected) in new[] { ("# ", "One"), ("## ", "Two"), ("#### ", "Four") })
        {
            var chunks = chunker.Chunk($"  {prefix}{expected}  \ntext follows here.", pageNumber: 1);
            Assert.Equal(expected, chunks[0].SectionTitle);
        }

        // Five hashes is not a heading.
        var five = chunker.Chunk("##### Five\ntext follows here.", pageNumber: 1);
        Assert.Null(five[0].SectionTitle);
    }

    // ── Overlap after a heading split ─────────────────────────────────────────

    [Fact]
    public void Chunk_AfterHeadingSplit_NextChunkStartsAtHeadingWithoutPreviousSectionText()
    {
        var chunker = new SlidingWindowTextChunker();
        var text = Sentences("first", 800) + "\n## Next Topic\n" + Sentences("second", 600);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        // Overlap is deliberately not carried back across a heading, or the heading would no
        // longer start the chunk.
        Assert.StartsWith("## Next Topic", chunks[1].Text);
        Assert.DoesNotContain("first sentence", chunks[1].Text);
    }

    [Fact]
    public void Chunk_LongSectionAfterHeadingSplit_StillOverlapsBy300()
    {
        var chunker = new SlidingWindowTextChunker();
        // A heading split first, then a long section of unstructured tokens that must hard-cut.
        var text = Sentences("first", 800) + "\n## Long Section\n" + Words(500);

        var chunks = chunker.Chunk(text, pageNumber: 1);

        Assert.True(chunks.Count >= 3, $"Expected at least 3 chunks, got {chunks.Count}.");
        Assert.StartsWith("## Long Section", chunks[1].Text);

        // chunk[2] begins ~300 characters before the end of chunk[1].
        var probe = chunks[2].Text[..250];
        var at = chunks[1].Text.IndexOf(probe, StringComparison.Ordinal);
        Assert.True(at >= 0, "chunk[2] should start inside chunk[1].");
        Assert.InRange(chunks[1].Text.Length - at, 290, 301);
    }
}
