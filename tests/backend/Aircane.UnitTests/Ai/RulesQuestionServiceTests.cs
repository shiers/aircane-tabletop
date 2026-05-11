using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

public class RulesQuestionServiceTests
{
    private readonly RulesQuestionService _service;
    private readonly FakeRagContextBuilder _fakeRag;
    private readonly FakeAiProvider _fakeAi;

    public RulesQuestionServiceTests()
    {
        _fakeRag = new FakeRagContextBuilder();
        _fakeAi = new FakeAiProvider();
        _service = new RulesQuestionService(
            _fakeRag,
            _fakeAi,
            NullLogger<RulesQuestionService>.Instance);
    }

    [Fact]
    public async Task AskAsync_WithRelevantSources_ReturnsAnswerWithCitations()
    {
        var citations = new List<RagCitation>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Player's Handbook", 14, "Combat")
        };
        _fakeRag.SetResult(new RagContextResult(
            ContextText: "[1] (Player's Handbook — Combat, p.14)\nAC is calculated from armor + dex.",
            Citations: citations,
            TotalChunksRetrieved: 1,
            ChunksIncluded: 1));

        var request = new RulesQuestionRequest(Question: "How is AC calculated?");
        var response = await _service.AskAsync(request);

        Assert.NotEmpty(response.Answer);
        Assert.True(response.HasSourceSupport);
        Assert.Single(response.Citations);
        Assert.Equal("Player's Handbook", response.Citations[0].SourceTitle);
        Assert.Equal(14, response.Citations[0].PageNumber);
        Assert.Equal("Combat", response.Citations[0].SectionTitle);
    }

    [Fact]
    public async Task AskAsync_WithNoSources_ReturnsAnswerWithoutSourceSupport()
    {
        _fakeRag.SetResult(new RagContextResult(
            ContextText: string.Empty,
            Citations: [],
            TotalChunksRetrieved: 0,
            ChunksIncluded: 0));

        var request = new RulesQuestionRequest(Question: "What is the range of Fireball?");
        var response = await _service.AskAsync(request);

        Assert.NotEmpty(response.Answer);
        Assert.False(response.HasSourceSupport);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task AskAsync_PassesGameSystemAndRulesetToRag()
    {
        _fakeRag.SetResult(new RagContextResult(
            ContextText: string.Empty,
            Citations: [],
            TotalChunksRetrieved: 0,
            ChunksIncluded: 0));

        var request = new RulesQuestionRequest(
            Question: "How does flanking work?",
            GameSystem: "D&D 5e 2014",
            Ruleset: "DMG");

        await _service.AskAsync(request);

        Assert.NotNull(_fakeRag.LastRequest);
        Assert.Equal("D&D 5e 2014", _fakeRag.LastRequest.GameSystem);
        Assert.Equal("DMG", _fakeRag.LastRequest.Ruleset);
    }

    [Fact]
    public async Task AskAsync_PassesQuestionAsRagQuery()
    {
        _fakeRag.SetResult(new RagContextResult(
            ContextText: string.Empty,
            Citations: [],
            TotalChunksRetrieved: 0,
            ChunksIncluded: 0));

        var request = new RulesQuestionRequest(Question: "What is opportunity attack?");
        await _service.AskAsync(request);

        Assert.NotNull(_fakeRag.LastRequest);
        Assert.Equal("What is opportunity attack?", _fakeRag.LastRequest.Query);
    }

    [Fact]
    public async Task AskAsync_ThrowsOnNullRequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.AskAsync(null!));
    }

    [Fact]
    public async Task AskAsync_ThrowsOnEmptyQuestion()
    {
        var request = new RulesQuestionRequest(Question: "");
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AskAsync(request));
    }

    [Fact]
    public async Task AskAsync_ThrowsOnWhitespaceQuestion()
    {
        var request = new RulesQuestionRequest(Question: "   ");
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AskAsync(request));
    }

    [Fact]
    public async Task AskAsync_WithLowConfidenceChunks_ReturnsNoSourceSupport()
    {
        // Simulate a scenario where chunks were retrieved but all had low scores
        // and were filtered out by the RAG builder (resulting in ChunksIncluded = 0)
        _fakeRag.SetResult(new RagContextResult(
            ContextText: string.Empty,
            Citations: [],
            TotalChunksRetrieved: 5,
            ChunksIncluded: 0));

        var request = new RulesQuestionRequest(Question: "How does flanking work?");
        var response = await _service.AskAsync(request);

        Assert.NotEmpty(response.Answer);
        Assert.False(response.HasSourceSupport);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public void AskAsync_WithLowConfidenceChunks_SystemPromptContainsCannotConfirm()
    {
        // When ChunksIncluded is 0, the system prompt should instruct AI to say it cannot confirm
        // We verify this through the static BuildSystemPrompt method which is used internally
        var prompt = RulesQuestionService.BuildSystemPrompt(hasSourceSupport: false);
        Assert.Contains("cannot confirm", prompt);
        Assert.Contains("not grounded", prompt);
        Assert.Contains("import the relevant rulebook", prompt);
    }

    [Fact]
    public async Task AskAsync_MultipleCitations_AllMappedCorrectly()
    {
        var chunkId1 = Guid.NewGuid();
        var chunkId2 = Guid.NewGuid();
        var citations = new List<RagCitation>
        {
            new(chunkId1, Guid.NewGuid(), "PHB", 241, "Spells"),
            new(chunkId2, Guid.NewGuid(), "XGE", 150, null)
        };
        _fakeRag.SetResult(new RagContextResult(
            ContextText: "[1] (PHB — Spells, p.241)\nFireball text.\n[2] (XGE, p.150)\nExtra text.",
            Citations: citations,
            TotalChunksRetrieved: 2,
            ChunksIncluded: 2));

        var request = new RulesQuestionRequest(Question: "Fireball details");
        var response = await _service.AskAsync(request);

        Assert.Equal(2, response.Citations.Count);
        Assert.Equal("PHB", response.Citations[0].SourceTitle);
        Assert.Equal(241, response.Citations[0].PageNumber);
        Assert.Equal("Spells", response.Citations[0].SectionTitle);
        Assert.Equal(chunkId1, response.Citations[0].ChunkId);

        Assert.Equal("XGE", response.Citations[1].SourceTitle);
        Assert.Equal(150, response.Citations[1].PageNumber);
        Assert.Null(response.Citations[1].SectionTitle);
        Assert.Equal(chunkId2, response.Citations[1].ChunkId);
    }

    [Fact]
    public void BuildSystemPrompt_WithSourceSupport_ContainsCitationInstructions()
    {
        var prompt = RulesQuestionService.BuildSystemPrompt(hasSourceSupport: true);

        Assert.Contains("retrieved rules context", prompt);
        Assert.Contains("[N]", prompt);
        Assert.Contains("official rules", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithoutSourceSupport_ContainsNoSourceWarning()
    {
        var prompt = RulesQuestionService.BuildSystemPrompt(hasSourceSupport: false);

        Assert.Contains("cannot confirm", prompt);
        Assert.Contains("import the relevant rulebook", prompt);
        Assert.Contains("not grounded", prompt);
        Assert.Contains("potentially inaccurate", prompt);
    }

    [Fact]
    public void BuildPromptMessages_WithSources_IncludesContextMessage()
    {
        var ragResult = new RagContextResult(
            ContextText: "Some context text",
            Citations: [new RagCitation(Guid.NewGuid(), Guid.NewGuid(), "PHB", 1, null)],
            TotalChunksRetrieved: 1,
            ChunksIncluded: 1);

        var messages = RulesQuestionService.BuildPromptMessages("My question", ragResult, hasSourceSupport: true);

        // System prompt + context + user question = 3 messages
        Assert.Equal(3, messages.Count());
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("system", messages[1].Role);
        Assert.Contains("Some context text", messages[1].Content);
        Assert.Equal("user", messages[2].Role);
        Assert.Equal("My question", messages[2].Content);
    }

    [Fact]
    public void BuildPromptMessages_WithoutSources_NoContextMessage()
    {
        var ragResult = new RagContextResult(
            ContextText: string.Empty,
            Citations: [],
            TotalChunksRetrieved: 0,
            ChunksIncluded: 0);

        var messages = RulesQuestionService.BuildPromptMessages("My question", ragResult, hasSourceSupport: false);

        // System prompt + user question = 2 messages (no context)
        Assert.Equal(2, messages.Count());
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("My question", messages[1].Content);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeRagContextBuilder : IRagContextBuilder
    {
        private RagContextResult _result = new(string.Empty, [], 0, 0);

        public RagContextRequest? LastRequest { get; private set; }

        public void SetResult(RagContextResult result) => _result = result;

        public Task<RagContextResult> BuildContextAsync(
            RagContextRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
