using Aircane.Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>Stat block handling in <see cref="SlidingWindowTextChunker"/>.</summary>
public class StatBlockChunkerTests
{
    private static readonly string Crocodile = StatBlockFixtures.GiantCrocodileLines;

    // ── Kept whole ────────────────────────────────────────────────────────────

    [Fact]
    public void Chunker_StatBlock_EmittedAsSingleChunk_NotSplitBySlidingWindow()
    {
        // A line-based block well over the 1100-character window.
        var block = StatBlockFixtures.LongBlock("Ancient Red Dragon", 600, 1500, 400, 700);
        Assert.InRange(block.Length, 2000, StatBlockDetector.MaxStatBlockChars);

        var chunks = new SlidingWindowTextChunker().Chunk(block, pageNumber: 1);

        var chunk = Assert.Single(chunks);
        Assert.Equal(block.Trim(), chunk.Text);
        Assert.True(chunk.Text.Length > 1100);
    }

    [Fact]
    public void Chunker_StatBlockChunk_HasChunkTypeStatBlock()
    {
        var chunks = new SlidingWindowTextChunker().Chunk(Crocodile, pageNumber: 1);

        Assert.Equal("stat-block", Assert.Single(chunks).ChunkType);
        Assert.Equal(SlidingWindowTextChunker.StatBlockChunkType, "stat-block");
    }

    [Fact]
    public void Chunker_StatBlockChunk_HasCreatureNameAsSectionTitle()
    {
        var chunks = new SlidingWindowTextChunker().Chunk(Crocodile, pageNumber: 1, sectionTitle: "Appendix");

        Assert.Equal("Giant Crocodile", Assert.Single(chunks).SectionTitle);
    }

    // ── Over the hard limit ───────────────────────────────────────────────────

    [Fact]
    public void Chunker_StatBlockExceeding4000Chars_SplitAtActionBoundary()
    {
        var block = StatBlockFixtures.LongBlock("Ancient Red Dragon", 500, 2000, 1000, 1500);
        Assert.True(block.Length > StatBlockDetector.MaxStatBlockChars);

        var chunks = new SlidingWindowTextChunker().Chunk(block, pageNumber: 1);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.Equal("stat-block", c.ChunkType));
        Assert.All(chunks, c => Assert.Equal("Ancient Red Dragon", c.SectionTitle));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= StatBlockDetector.MaxStatBlockChars));
        // Cut at a section label, never mid-sentence.
        Assert.StartsWith("Legendary Actions", chunks[1].Text);
        Assert.EndsWith("piercing damage.", chunks[0].Text);
    }

    [Fact]
    public void Chunker_StatBlockExceeding4000Chars_LogsWarning()
    {
        var logger = new CapturingLogger();
        var chunker = new SlidingWindowTextChunker(logger: logger);
        var block = StatBlockFixtures.LongBlock("Ancient Red Dragon", 500, 2000, 1000, 1500);

        chunker.Chunk(block, pageNumber: 7);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Ancient Red Dragon", warning.Message);
        Assert.Contains("4000", warning.Message);
    }

    [Fact]
    public void Chunker_StatBlockWithinLimit_DoesNotLogAWarning()
    {
        var logger = new CapturingLogger();

        new SlidingWindowTextChunker(logger: logger).Chunk(Crocodile, pageNumber: 1);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    // ── Mixed text ────────────────────────────────────────────────────────────

    [Fact]
    public void Chunker_MixedText_ProseChunkedNormally_StatBlocksKeptWhole()
    {
        var text = StatBlockFixtures.Prose("first", 2600) + "\n\n" +
                   Crocodile + "\n" +
                   "## Chapter 2\n" +
                   StatBlockFixtures.Prose("second", 2600);

        var chunks = new SlidingWindowTextChunker().Chunk(text, pageNumber: 4);

        var stat = chunks.Where(c => c.ChunkType == "stat-block").ToList();
        var prose = chunks.Where(c => c.ChunkType is null).ToList();

        Assert.Single(stat);
        Assert.Contains("Tail. Melee Weapon Attack", stat[0].Text);
        Assert.True(prose.Count >= 4, $"Expected several windowed prose chunks, got {prose.Count}.");
        Assert.All(prose, c => Assert.True(c.Text.Length <= 1100));
        Assert.DoesNotContain(prose, c => c.Text.Contains("Giant Crocodile"));
        // Page, order and index are sequential across segments.
        Assert.All(chunks, c => Assert.Equal(4, c.PageNumber));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
        Assert.Equal("Giant Crocodile", chunks[chunks.ToList().IndexOf(stat[0])].SectionTitle);
    }

    [Fact]
    public void Chunker_ProseAfterAStatBlock_DoesNotInheritTheCreatureName()
    {
        var text = "# Marsh Creatures\n" + StatBlockFixtures.Prose("intro", 300) + "\n" +
                   Crocodile + "\n## Aftermath\n" + StatBlockFixtures.Prose("after", 300);

        var chunks = new SlidingWindowTextChunker().Chunk(text, pageNumber: 1);

        Assert.Equal("Marsh Creatures", chunks[0].SectionTitle);
        var last = chunks[^1];
        Assert.Null(last.ChunkType);
        Assert.Equal("Aftermath", last.SectionTitle);
    }

    [Fact]
    public void Chunker_MultipleStatBlocks_EachEmittedAsSingleChunk()
    {
        var text = Crocodile + "\n" +
                   "Wolf\nMedium beast, unaligned\nArmor Class 13\nHit Points 11 (2d8 + 2)\nActions\nBite. Hit: 7 piercing damage.\n" +
                   "Goblin\nSmall humanoid (goblinoid), neutral evil\nArmor Class 15\nHit Points 7 (2d6)\nActions\nScimitar. Hit: 5 slashing damage.";

        var chunks = new SlidingWindowTextChunker().Chunk(text, pageNumber: 1);

        Assert.Equal(["Giant Crocodile", "Wolf", "Goblin"], chunks.Select(c => c.SectionTitle));
        Assert.All(chunks, c => Assert.Equal("stat-block", c.ChunkType));
        Assert.Contains("Bite. Hit: 7", chunks[1].Text);
        Assert.DoesNotContain("Goblin", chunks[1].Text);
    }

    [Fact]
    public void Chunker_TextWithoutStatBlocks_IsUnchanged()
    {
        var text = StatBlockFixtures.Prose("plain", 3000);

        var chunks = new SlidingWindowTextChunker().Chunk(text, pageNumber: 1);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.Null(c.ChunkType));
    }

    // ── The regression this feature fixes ─────────────────────────────────────

    [Fact]
    public void Chunker_GiantCrocodileText_TailAttackInSameChunkAsBiteAttack()
    {
        var page = StatBlockFixtures.GiantCrocodilePage;

        var chunks = new SlidingWindowTextChunker().Chunk(page, pageNumber: 325);

        var withBite = chunks.Where(c => c.Text.Contains("Bite. Melee Weapon Attack")).ToList();
        var withTail = chunks.Where(c => c.Text.Contains("Tail. Melee Weapon Attack")).ToList();

        // Both attacks live in the same, single chunk, and it is the crocodile stat block.
        var chunk = Assert.Single(withBite);
        Assert.Same(chunk, Assert.Single(withTail));
        Assert.Equal("stat-block", chunk.ChunkType);
        Assert.Equal("Giant Crocodile", chunk.SectionTitle);
        Assert.Contains("Multiattack. The crocodile makes two attacks: one with its bite and one with its tail.", chunk.Text);
        Assert.Contains("Hold Breath", chunk.Text);
        // The whole block, from its stats to its last attack, is in that one chunk.
        Assert.Contains("Armor Class 14", chunk.Text);
        Assert.Contains("Hit Points 85", chunk.Text);

        // Control: with detection disabled (the header's "Armor Class" disguised) the same page goes through
        // plain sliding-window chunking, and no single chunk holds both the creature's stats and its Tail
        // attack. (With the 300-character overlap the Bite and Tail can still share a later chunk, which
        // then has no stats, so the model sees an incomplete entry.)
        var disguised = page.Replace("Armor Class", "Armour Cls");
        var windowed = new SlidingWindowTextChunker().Chunk(disguised, pageNumber: 325);
        Assert.All(windowed, c => Assert.Null(c.ChunkType));
        Assert.Contains(windowed, c => c.Text.Contains("Armour Cls 14"));
        Assert.DoesNotContain(windowed, c => c.Text.Contains("Armour Cls 14") && c.Text.Contains("Tail. Melee Weapon Attack"));
        // The next creature is its own chunk.
        Assert.Contains(chunks, c => c.SectionTitle == "Giant Eagle" && c.ChunkType == "stat-block");
    }

    [Fact]
    public void Chunker_GiantCrocodileText_LineBasedForm_KeptWhole()
    {
        var chunks = new SlidingWindowTextChunker(maxChunkSize: 300, overlapSize: 50)
            .Chunk(StatBlockFixtures.GiantCrocodileLines, pageNumber: 1);

        var chunk = Assert.Single(chunks);
        Assert.Contains("Bite.", chunk.Text);
        Assert.Contains("Tail.", chunk.Text);
    }

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class CapturingLogger : ILogger<SlidingWindowTextChunker>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
