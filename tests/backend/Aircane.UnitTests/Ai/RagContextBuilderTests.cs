using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.Retrieval;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

public class RagContextBuilderTests
{
    private readonly IRagContextBuilder _builder;
    private readonly FakeRetrievalService _fakeRetrieval;

    public RagContextBuilderTests()
    {
        _fakeRetrieval = new FakeRetrievalService();
        _builder = new RagContextBuilder(
            _fakeRetrieval,
            NullLogger<RagContextBuilder>.Instance);
    }

    [Fact]
    public async Task BuildContextAsync_WithNoResults_ReturnsEmptyContext()
    {
        _fakeRetrieval.SetResults([]);

        var request = new RagContextRequest(Query: "What is AC?");
        var result = await _builder.BuildContextAsync(request);

        Assert.Empty(result.ContextText);
        Assert.Empty(result.Citations);
        Assert.Equal(0, result.TotalChunksRetrieved);
        Assert.Equal(0, result.ChunksIncluded);
    }

    [Fact]
    public async Task BuildContextAsync_WithChunks_IncludesTextAndCitations()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "Player's Handbook",
                ChunkIndex: 0,
                Text: "Armor Class (AC) represents how well a character avoids being hit.",
                PageNumber: 14,
                SectionTitle: "Combat",
                Visibility: ContentVisibility.Public,
                Score: 0.95),
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "Dungeon Master's Guide",
                ChunkIndex: 3,
                Text: "A creature's AC is determined by its armor, shield, and Dexterity modifier.",
                PageNumber: 8,
                SectionTitle: null,
                Visibility: ContentVisibility.Public,
                Score: 0.85)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "What is AC?");
        var result = await _builder.BuildContextAsync(request);

        Assert.NotEmpty(result.ContextText);
        Assert.Equal(2, result.Citations.Count);
        Assert.Equal(2, result.TotalChunksRetrieved);
        Assert.Equal(2, result.ChunksIncluded);

        // Verify citations contain correct source info
        Assert.Equal("Player's Handbook", result.Citations[0].SourceDocumentTitle);
        Assert.Equal(14, result.Citations[0].PageNumber);
        Assert.Equal("Combat", result.Citations[0].SectionTitle);

        Assert.Equal("Dungeon Master's Guide", result.Citations[1].SourceDocumentTitle);
        Assert.Equal(8, result.Citations[1].PageNumber);
        Assert.Null(result.Citations[1].SectionTitle);
    }

    [Fact]
    public async Task BuildContextAsync_RespectsCharacterBudget()
    {
        // Create chunks that together exceed a small budget
        var chunks = new List<ChunkResultDto>();
        for (int i = 0; i < 10; i++)
        {
            chunks.Add(new ChunkResultDto(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: $"Source {i}",
                ChunkIndex: i,
                Text: $"Chunk {i} " + new string((char)('a' + i), 200), // distinct 200+ char texts
                PageNumber: i + 1,
                SectionTitle: null,
                Visibility: ContentVisibility.Public,
                Score: 1.0 - (i * 0.1)));
        }
        _fakeRetrieval.SetResults(chunks);

        // Set a small budget that can only fit a few chunks
        var request = new RagContextRequest(Query: "test", MaxContextChars: 800);
        var result = await _builder.BuildContextAsync(request);

        Assert.True(result.ChunksIncluded < 10);
        Assert.True(result.ContextText.Length <= 800);
        Assert.Equal(10, result.TotalChunksRetrieved);
    }

    [Fact]
    public async Task BuildContextAsync_ContextContainsCitationMarkers()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Fireball deals 8d6 fire damage.",
                PageNumber: 241,
                SectionTitle: "Spells",
                Visibility: ContentVisibility.Public,
                Score: 0.9)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "fireball damage");
        var result = await _builder.BuildContextAsync(request);

        // Should contain citation marker [1]
        Assert.Contains("[1]", result.ContextText);
        // Should contain source reference
        Assert.Contains("PHB", result.ContextText);
        Assert.Contains("Spells", result.ContextText);
        Assert.Contains("p.241", result.ContextText);
        // Should contain the chunk text
        Assert.Contains("Fireball deals 8d6 fire damage.", result.ContextText);
    }

    [Fact]
    public async Task BuildContextAsync_ContextContainsCitationSummary()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "Player's Handbook",
                ChunkIndex: 0,
                Text: "Some rule text here.",
                PageNumber: 50,
                SectionTitle: "Actions",
                Visibility: ContentVisibility.Public,
                Score: 0.9)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "actions in combat");
        var result = await _builder.BuildContextAsync(request);

        // Should contain a Sources section
        Assert.Contains("Sources:", result.ContextText);
        Assert.Contains("Player's Handbook", result.ContextText);
    }

    [Theory]
    [InlineData(ParticipantRole.Host, ContentVisibility.DMOnly)]
    [InlineData(ParticipantRole.HumanDm, ContentVisibility.DMOnly)]
    [InlineData(ParticipantRole.Player, ContentVisibility.Public)]
    [InlineData(ParticipantRole.Spectator, ContentVisibility.Public)]
    public void GetMaxVisibility_ReturnsCorrectVisibilityForRole(
        ParticipantRole role, ContentVisibility expected)
    {
        var result = RagContextBuilder.GetMaxVisibility(role);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task BuildContextAsync_PassesCorrectVisibilityToRetrieval()
    {
        _fakeRetrieval.SetResults([]);

        var request = new RagContextRequest(
            Query: "test",
            RequesterRole: ParticipantRole.Player);

        await _builder.BuildContextAsync(request);

        Assert.Equal(ContentVisibility.Public, _fakeRetrieval.LastMaxVisibility);
    }

    [Fact]
    public async Task BuildContextAsync_PassesGameSystemAndRulesetToRetrieval()
    {
        _fakeRetrieval.SetResults([]);

        var request = new RagContextRequest(
            Query: "test",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB");

        await _builder.BuildContextAsync(request);

        Assert.Equal("D&D 5e 2014", _fakeRetrieval.LastSearchRequest?.GameSystem);
        Assert.Equal("PHB", _fakeRetrieval.LastSearchRequest?.Ruleset);
    }

    [Fact]
    public async Task BuildContextAsync_WithDmRole_PassesDmOnlyVisibility()
    {
        _fakeRetrieval.SetResults([]);

        var request = new RagContextRequest(
            Query: "hidden trap details",
            RequesterRole: ParticipantRole.HumanDm);

        await _builder.BuildContextAsync(request);

        Assert.Equal(ContentVisibility.DMOnly, _fakeRetrieval.LastMaxVisibility);
    }

    [Theory]
    [InlineData(SourceType.Homebrew, 0)]
    [InlineData(SourceType.Rules, 1)]
    [InlineData(SourceType.Adventure, 2)]
    [InlineData(SourceType.Solo, 3)]
    [InlineData(SourceType.Generated, 4)]
    [InlineData(SourceType.Character, 5)]
    [InlineData(SourceType.Unknown, 6)]
    public void GetSourceTypePriority_ReturnsCorrectOrder(SourceType sourceType, int expectedPriority)
    {
        var priority = RagContextBuilder.GetSourceTypePriority(sourceType);
        Assert.Equal(expectedPriority, priority);
    }

    [Fact]
    public void ApplySourcePriorityWithType_SortsHomebrewFirst()
    {
        var chunks = new List<PrioritizedChunk>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Rules Source", 0, "Rule text", 1, null, ContentVisibility.Public, 0.9, SourceType.Rules),
            new(Guid.NewGuid(), Guid.NewGuid(), "House Rules", 0, "House rule text", 1, null, ContentVisibility.Public, 0.8, SourceType.Homebrew),
            new(Guid.NewGuid(), Guid.NewGuid(), "Adventure", 0, "Adventure text", 1, null, ContentVisibility.Public, 0.95, SourceType.Adventure),
        };

        var result = RagContextBuilder.ApplySourcePriorityWithType(chunks);

        Assert.Equal(SourceType.Homebrew, result[0].SourceType);
        Assert.Equal(SourceType.Rules, result[1].SourceType);
        Assert.Equal(SourceType.Adventure, result[2].SourceType);
    }

    [Fact]
    public void ApplySourcePriorityWithType_PreservesScoreOrderWithinSamePriority()
    {
        var chunks = new List<PrioritizedChunk>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "PHB", 0, "Low score rule", 1, null, ContentVisibility.Public, 0.5, SourceType.Rules),
            new(Guid.NewGuid(), Guid.NewGuid(), "DMG", 0, "High score rule", 1, null, ContentVisibility.Public, 0.9, SourceType.Rules),
        };

        var result = RagContextBuilder.ApplySourcePriorityWithType(chunks);

        // Higher score should come first within same priority tier
        Assert.Equal("DMG", result[0].SourceDocumentTitle);
        Assert.Equal("PHB", result[1].SourceDocumentTitle);
    }

    [Fact]
    public async Task BuildContextAsync_ThrowsOnNullRequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _builder.BuildContextAsync(null!));
    }

    [Fact]
    public async Task BuildContextAsync_ThrowsOnEmptyQuery()
    {
        var request = new RagContextRequest(Query: "");
        await Assert.ThrowsAsync<ArgumentException>(
            () => _builder.BuildContextAsync(request));
    }

    [Fact]
    public async Task BuildContextAsync_ThrowsOnWhitespaceQuery()
    {
        var request = new RagContextRequest(Query: "   ");
        await Assert.ThrowsAsync<ArgumentException>(
            () => _builder.BuildContextAsync(request));
    }

    [Fact]
    public async Task BuildContextAsync_LowScoreChunks_FilteredOut_ReturnsEmptyContext()
    {
        // All chunks have scores below the default threshold of 0.3
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Some irrelevant text.",
                PageNumber: 10,
                SectionTitle: "Combat",
                Visibility: ContentVisibility.Public,
                Score: 0.1),
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "DMG",
                ChunkIndex: 1,
                Text: "Another irrelevant text.",
                PageNumber: 20,
                SectionTitle: null,
                Visibility: ContentVisibility.Public,
                Score: 0.2)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "What is AC?", MinRelevanceScore: 0.3);
        var result = await _builder.BuildContextAsync(request);

        Assert.Empty(result.ContextText);
        Assert.Empty(result.Citations);
        Assert.Equal(2, result.TotalChunksRetrieved);
        Assert.Equal(0, result.ChunksIncluded);
    }

    [Fact]
    public async Task BuildContextAsync_MixedScoreChunks_OnlyHighScoreIncluded()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Relevant rule text.",
                PageNumber: 14,
                SectionTitle: "Combat",
                Visibility: ContentVisibility.Public,
                Score: 0.8),
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "DMG",
                ChunkIndex: 1,
                Text: "Low relevance text.",
                PageNumber: 20,
                SectionTitle: null,
                Visibility: ContentVisibility.Public,
                Score: 0.15)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "What is AC?", MinRelevanceScore: 0.3);
        var result = await _builder.BuildContextAsync(request);

        Assert.NotEmpty(result.ContextText);
        Assert.Single(result.Citations);
        Assert.Equal(2, result.TotalChunksRetrieved);
        Assert.Equal(1, result.ChunksIncluded);
        Assert.Equal("PHB", result.Citations[0].SourceDocumentTitle);
    }

    [Fact]
    public async Task BuildContextAsync_NullScoreChunks_AreRetained()
    {
        // Chunks with null scores (e.g., from keyword-only search) should not be filtered out
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Keyword match text.",
                PageNumber: 14,
                SectionTitle: "Combat",
                Visibility: ContentVisibility.Public,
                Score: null)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "What is AC?", MinRelevanceScore: 0.5);
        var result = await _builder.BuildContextAsync(request);

        Assert.NotEmpty(result.ContextText);
        Assert.Single(result.Citations);
        Assert.Equal(1, result.ChunksIncluded);
    }

    [Fact]
    public async Task BuildContextAsync_ZeroMinScore_DisablesFiltering()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Low score text.",
                PageNumber: 14,
                SectionTitle: "Combat",
                Visibility: ContentVisibility.Public,
                Score: 0.05)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "What is AC?", MinRelevanceScore: 0.0);
        var result = await _builder.BuildContextAsync(request);

        Assert.NotEmpty(result.ContextText);
        Assert.Single(result.Citations);
        Assert.Equal(1, result.ChunksIncluded);
    }

    [Theory]
    [InlineData(0.29, true)]  // Just below threshold - filtered out
    [InlineData(0.30, true)]  // Exactly at threshold - included
    [InlineData(0.31, true)]  // Just above threshold - included
    public async Task BuildContextAsync_ScoreAtThresholdBoundary_BehavesCorrectly(double score, bool _)
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Boundary test text.",
                PageNumber: 14,
                SectionTitle: "Combat",
                Visibility: ContentVisibility.Public,
                Score: score)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "test", MinRelevanceScore: 0.3);
        var result = await _builder.BuildContextAsync(request);

        if (score >= 0.3)
        {
            Assert.Equal(1, result.ChunksIncluded);
        }
        else
        {
            Assert.Equal(0, result.ChunksIncluded);
        }
    }

    [Fact]
    public void FilterByMinRelevanceScore_WithZeroThreshold_ReturnsAllChunks()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "PHB", 0, "Text", 1, null, ContentVisibility.Public, 0.01),
            new(Guid.NewGuid(), Guid.NewGuid(), "DMG", 1, "Text", 2, null, ContentVisibility.Public, 0.5),
        };

        var result = RagContextBuilder.FilterByMinRelevanceScore(chunks, 0.0);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterByMinRelevanceScore_WithHighThreshold_FiltersLowScores()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "PHB", 0, "Text", 1, null, ContentVisibility.Public, 0.2),
            new(Guid.NewGuid(), Guid.NewGuid(), "DMG", 1, "Text", 2, null, ContentVisibility.Public, 0.8),
            new(Guid.NewGuid(), Guid.NewGuid(), "XGE", 2, "Text", 3, null, ContentVisibility.Public, 0.4),
        };

        var result = RagContextBuilder.FilterByMinRelevanceScore(chunks, 0.5);

        Assert.Single(result);
        Assert.Equal("DMG", result[0].SourceDocumentTitle);
    }

    [Fact]
    public void FilterByMinRelevanceScore_NullScores_AreRetained()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "PHB", 0, "Text", 1, null, ContentVisibility.Public, null),
            new(Guid.NewGuid(), Guid.NewGuid(), "DMG", 1, "Text", 2, null, ContentVisibility.Public, 0.1),
        };

        var result = RagContextBuilder.FilterByMinRelevanceScore(chunks, 0.5);

        Assert.Single(result);
        Assert.Equal("PHB", result[0].SourceDocumentTitle);
    }

    [Fact]
    public async Task BuildContextAsync_ChunkWithSectionAndPage_FormatsCorrectly()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "PHB",
                ChunkIndex: 0,
                Text: "Some text.",
                PageNumber: 100,
                SectionTitle: "Chapter 9",
                Visibility: ContentVisibility.Public,
                Score: 0.9)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "test");
        var result = await _builder.BuildContextAsync(request);

        Assert.Contains("PHB - Chapter 9, p.100", result.ContextText);
    }

    [Fact]
    public async Task BuildContextAsync_ChunkWithNoSectionOrPage_FormatsWithTitleOnly()
    {
        var chunks = new List<ChunkResultDto>
        {
            new(
                ChunkId: Guid.NewGuid(),
                SourceDocumentId: Guid.NewGuid(),
                SourceDocumentTitle: "Homebrew Doc",
                ChunkIndex: 0,
                Text: "Custom rule.",
                PageNumber: null,
                SectionTitle: null,
                Visibility: ContentVisibility.Public,
                Score: 0.9)
        };
        _fakeRetrieval.SetResults(chunks);

        var request = new RagContextRequest(Query: "test");
        var result = await _builder.BuildContextAsync(request);

        Assert.Contains("(Homebrew Doc)", result.ContextText);
    }

    // ── Source priority ───────────────────────────────────────────────────────

    private static ChunkResultDto Chunk(string title, string text, double score, SourceType type) =>
        new(Guid.NewGuid(), Guid.NewGuid(), title, 0, text, 1, null, ContentVisibility.Public, score, type);

    [Fact]
    public void ApplySourcePriority_OrdersBySourceTypeThenScore()
    {
        var chunks = new[]
        {
            Chunk("Unknown", "u", 0.99, SourceType.Unknown),
            Chunk("Generated", "g", 0.95, SourceType.Generated),
            Chunk("Solo", "s", 0.90, SourceType.Solo),
            Chunk("Adventure", "a", 0.85, SourceType.Adventure),
            Chunk("RulesLow", "r2", 0.60, SourceType.Rules),
            Chunk("RulesHigh", "r1", 0.80, SourceType.Rules),
            Chunk("Homebrew", "h", 0.50, SourceType.Homebrew),
        };

        var ordered = RagContextBuilder.ApplySourcePriority(chunks).Select(c => c.SourceDocumentTitle);

        Assert.Equal(
            ["Homebrew", "RulesHigh", "RulesLow", "Adventure", "Solo", "Generated", "Unknown"],
            ordered);
    }

    [Fact]
    public async Task BuildContextAsync_HomebrewOutranksRules()
    {
        _fakeRetrieval.SetResults([
            Chunk("Core Rules", "Grappling uses a contested check against the target.", 0.9, SourceType.Rules),
            Chunk("House Rules", "In our game, grappling needs a bonus action instead.", 0.5, SourceType.Homebrew),
        ]);

        var result = await _builder.BuildContextAsync(new RagContextRequest(Query: "grapple"));

        Assert.Equal("House Rules", result.Citations[0].SourceDocumentTitle);
        Assert.Equal("Core Rules", result.Citations[1].SourceDocumentTitle);
    }

    // ── Deduplication ─────────────────────────────────────────────────────────

    private const string LongText =
        "A creature that is grappled has the Grappled condition and its speed becomes zero. " +
        "The condition ends if the grappler is incapacitated or if the grappled creature is moved out of reach.";

    [Fact]
    public async Task BuildContextAsync_SkipsChunkThatMostlyRepeatsAnEarlierOne()
    {
        _fakeRetrieval.SetResults([
            Chunk("PHB", LongText, 0.9, SourceType.Rules),
            Chunk("PHB", LongText.Substring(10) + " Extra trailing words here.", 0.8, SourceType.Rules),
            Chunk("DMG", "Completely different text about travel pace and exhaustion over long journeys.", 0.7, SourceType.Rules),
        ]);

        var result = await _builder.BuildContextAsync(new RagContextRequest(Query: "grapple"));

        Assert.Equal(2, result.ChunksIncluded);
        Assert.Equal(["PHB", "DMG"], result.Citations.Select(c => c.SourceDocumentTitle));
    }

    [Fact]
    public void RemoveDuplicateChunks_KeepsChunksSharingLittleText()
    {
        // Adjacent sliding-window chunks share only ~10% of their text, below the 40% threshold.
        var shared = new string('s', 200);
        var a = new string('a', 1800) + shared;
        var b = shared + new string('b', 1800);

        var kept = RagContextBuilder.RemoveDuplicateChunks([
            Chunk("A", a, 0.9, SourceType.Rules),
            Chunk("B", b, 0.8, SourceType.Rules)]);

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void OverlapFraction_IdenticalTexts_IsOne()
    {
        Assert.Equal(1.0, RagContextBuilder.OverlapFraction(LongText, LongText), 6);
    }

    [Fact]
    public void OverlapFraction_UnrelatedTexts_IsZero()
    {
        Assert.Equal(0.0, RagContextBuilder.OverlapFraction(LongText, new string('z', 300)), 6);
    }

    // ── Reranking ─────────────────────────────────────────────────────────────

    private const string TextA = "Grappling: a grappled creature has speed zero and the condition ends when the grappler is incapacitated or moved away.";
    private const string TextANearCopy = "Grappling: a grappled creature has speed zero and the condition ends when the grappler is incapacitated or moved far away.";
    private const string TextB = "Opportunity attacks: a creature can use its reaction to strike a foe that leaves its reach without disengaging first.";
    private const string TextC = "The Actor feat: gain advantage on Deception and Performance checks when impersonating someone you have listened to for a minute.";

    private RagContextBuilder BuilderWith(FakeRerankingProvider reranker) =>
        new(_fakeRetrieval, NullLogger<RagContextBuilder>.Instance, reranker);

    private static string[] Titles(RagContextResult result) =>
        result.Citations.Select(c => c.SourceDocumentTitle).ToArray();

    [Fact]
    public async Task RagContextBuilder_SkipsReranking_WhenProviderIsNone()
    {
        var reranker = new FakeRerankingProvider("None") { ScoresByText = { [TextC] = 9f } };
        _fakeRetrieval.SetResults([
            Chunk("A", TextA, 0.9, SourceType.Rules),
            Chunk("C", TextC, 0.8, SourceType.Rules)]);

        var result = await BuilderWith(reranker).BuildContextAsync(
            new RagContextRequest(Query: "q", MinRelevanceScore: 0));

        Assert.Equal(0, reranker.CallCount);
        Assert.Equal(["A", "C"], Titles(result));
    }

    [Fact]
    public async Task RagContextBuilder_SkipsReranking_WhenProviderIsUnavailable()
    {
        var reranker = new FakeRerankingProvider("Cohere") { Available = false, ScoresByText = { [TextC] = 9f } };
        _fakeRetrieval.SetResults([
            Chunk("A", TextA, 0.9, SourceType.Rules),
            Chunk("C", TextC, 0.8, SourceType.Rules)]);

        var result = await BuilderWith(reranker).BuildContextAsync(
            new RagContextRequest(Query: "q", MinRelevanceScore: 0));

        Assert.Equal(0, reranker.CallCount);
        Assert.Equal(["A", "C"], Titles(result));
    }

    [Fact]
    public async Task RagContextBuilder_ReordersByRerankScore_WhenProviderIsNotNone()
    {
        var reranker = new FakeRerankingProvider("Cohere")
        {
            ScoresByText = { [TextC] = 0.95f, [TextA] = 0.40f, [TextB] = 0.10f },
        };
        _fakeRetrieval.SetResults([
            Chunk("A", TextA, 0.9, SourceType.Rules),
            Chunk("B", TextB, 0.8, SourceType.Rules),
            Chunk("C", TextC, 0.7, SourceType.Rules)]);

        var result = await BuilderWith(reranker).BuildContextAsync(
            new RagContextRequest(Query: "explain the actor feat", MinRelevanceScore: 0));

        Assert.Equal(1, reranker.CallCount);
        Assert.Equal("explain the actor feat", reranker.LastQuery);
        Assert.Equal(["C", "A", "B"], Titles(result));
    }

    [Fact]
    public async Task RagContextBuilder_ReranksBeforeDedup_WhenProviderIsNotNone()
    {
        // A and A2 are near-duplicates. Retrieval ranks A first, but the reranker prefers A2. Because
        // reranking runs before dedup, A2 (the better-reranked copy) is the one kept.
        var reranker = new FakeRerankingProvider("Cohere")
        {
            ScoresByText = { [TextANearCopy] = 0.9f, [TextA] = 0.5f, [TextB] = 0.3f },
        };
        _fakeRetrieval.SetResults([
            Chunk("A", TextA, 0.9, SourceType.Rules),
            Chunk("A2", TextANearCopy, 0.8, SourceType.Rules),
            Chunk("B", TextB, 0.7, SourceType.Rules)]);

        var result = await BuilderWith(reranker).BuildContextAsync(
            new RagContextRequest(Query: "q", MinRelevanceScore: 0));

        Assert.Equal(["A2", "B"], Titles(result));
        Assert.Equal(3, result.TotalChunksRetrieved);
    }

    [Fact]
    public async Task RagContextBuilder_RerankedOrderPreservedAfterDedup()
    {
        var reranker = new FakeRerankingProvider("Cohere")
        {
            ScoresByText = { [TextC] = 0.9f, [TextA] = 0.6f, [TextANearCopy] = 0.55f, [TextB] = 0.2f },
        };
        _fakeRetrieval.SetResults([
            Chunk("A", TextA, 0.9, SourceType.Rules),
            Chunk("B", TextB, 0.8, SourceType.Rules),
            Chunk("A2", TextANearCopy, 0.7, SourceType.Rules),
            Chunk("C", TextC, 0.6, SourceType.Rules)]);

        var result = await BuilderWith(reranker).BuildContextAsync(
            new RagContextRequest(Query: "q", MinRelevanceScore: 0));

        // C first (reranked top), then A (the dup A2 is dropped), then B.
        Assert.Equal(["C", "A", "B"], Titles(result));
    }

    [Fact]
    public async Task RagContextBuilder_ChunksTheRerankerDoesNotScore_SortLast()
    {
        var reranker = new FakeRerankingProvider("Cohere") { ScoresByText = { [TextC] = 0.5f } };
        _fakeRetrieval.SetResults([
            Chunk("A", TextA, 0.9, SourceType.Rules),
            Chunk("B", TextB, 0.8, SourceType.Rules),
            Chunk("C", TextC, 0.7, SourceType.Rules)]);

        var result = await BuilderWith(reranker).BuildContextAsync(
            new RagContextRequest(Query: "q", MinRelevanceScore: 0));

        Assert.Equal(["C", "A", "B"], Titles(result));
    }

    [Fact]
    public async Task RagContextBuilder_SingleChunk_NotSentToReranker()
    {
        var reranker = new FakeRerankingProvider("Cohere");
        _fakeRetrieval.SetResults([Chunk("A", TextA, 0.9, SourceType.Rules)]);

        await BuilderWith(reranker).BuildContextAsync(new RagContextRequest(Query: "q", MinRelevanceScore: 0));

        Assert.Equal(0, reranker.CallCount);
    }

    private sealed class FakeRerankingProvider(string providerId) : IRerankingProvider
    {
        public string ProviderId => providerId;
        public bool Available { get; init; } = true;
        public bool IsAvailable => Available;
        public Dictionary<string, float> ScoresByText { get; } = new();
        public int CallCount { get; private set; }
        public string? LastQuery { get; private set; }

        public Task<IReadOnlyList<RerankResult>> RerankAsync(
            string query, IReadOnlyList<RerankCandidate> candidates, CancellationToken ct = default)
        {
            CallCount++;
            LastQuery = query;
            IReadOnlyList<RerankResult> results = candidates
                .Where(c => ScoresByText.ContainsKey(c.Text))
                .Select(c => new RerankResult(c.Id, ScoresByText[c.Text]))
                .ToList();
            return Task.FromResult(results);
        }

        public Task<RerankingConnectionResult> CheckConnectionAsync(CancellationToken ct = default) =>
            Task.FromResult(new RerankingConnectionResult(true, "ok"));
    }

    /// <summary>
    /// Fake retrieval service for testing the RAG context builder in isolation.
    /// </summary>
    private sealed class FakeRetrievalService : IRetrievalService
    {
        private IReadOnlyList<ChunkResultDto> _results = [];

        public SearchRequest? LastSearchRequest { get; private set; }
        public ContentVisibility? LastMaxVisibility { get; private set; }

        public void SetResults(IReadOnlyList<ChunkResultDto> results) => _results = results;

        public Task<IReadOnlyList<ChunkResultDto>> SearchByKeywordAsync(
            SearchRequest request, ContentVisibility maxVisibility, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            LastMaxVisibility = maxVisibility;
            return Task.FromResult(_results);
        }

        public Task<IReadOnlyList<ChunkResultDto>> SearchByVectorAsync(
            SearchRequest request, ContentVisibility maxVisibility, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            LastMaxVisibility = maxVisibility;
            return Task.FromResult(_results);
        }

        public Task<IReadOnlyList<ChunkResultDto>> SearchAsync(
            SearchRequest request, ContentVisibility maxVisibility, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            LastMaxVisibility = maxVisibility;
            return Task.FromResult(_results);
        }

        public Task<IReadOnlyList<string>> GetAvailableRulesetsAsync(
            string? gameSystem, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
