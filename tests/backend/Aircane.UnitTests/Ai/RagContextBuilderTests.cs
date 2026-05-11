using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
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
                Text: new string('x', 200), // 200 chars each
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
    [InlineData(0.29, true)]  // Just below threshold — filtered out
    [InlineData(0.30, true)]  // Exactly at threshold — included
    [InlineData(0.31, true)]  // Just above threshold — included
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

        Assert.Contains("PHB — Chapter 9, p.100", result.ContextText);
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
    }
}
