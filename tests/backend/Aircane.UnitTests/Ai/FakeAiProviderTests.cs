using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Ai;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="FakeAiProvider"/>.
/// </summary>
public class FakeAiProviderTests
{
    private readonly FakeAiProvider _provider = new();

    // ── Provider metadata ─────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_ReturnsFake()
    {
        Assert.Equal("Fake", _provider.ProviderName);
    }

    // ── ChatCompletionAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_WithUserMessage_EchoesContent()
    {
        var messages = new[]
        {
            AiMessage.System("You are a DM."),
            AiMessage.User("What is the AC of a goblin?"),
        };

        var response = await _provider.ChatCompletionAsync(messages);

        Assert.Contains("What is the AC of a goblin?", response);
    }

    [Fact]
    public async Task ChatCompletionAsync_WithUserMessage_IncludesFakePrefix()
    {
        var messages = new[] { AiMessage.User("Hello") };

        var response = await _provider.ChatCompletionAsync(messages);

        Assert.StartsWith("[Fake AI]", response);
    }

    [Fact]
    public async Task ChatCompletionAsync_UsesLastUserMessage_WhenMultiplePresent()
    {
        var messages = new[]
        {
            AiMessage.User("First question"),
            AiMessage.Assistant("First answer"),
            AiMessage.User("Second question"),
        };

        var response = await _provider.ChatCompletionAsync(messages);

        Assert.Contains("Second question", response);
        Assert.DoesNotContain("First question", response);
    }

    [Fact]
    public async Task ChatCompletionAsync_NoUserMessage_ReturnsFallback()
    {
        var messages = new[] { AiMessage.System("System only") };

        var response = await _provider.ChatCompletionAsync(messages);

        Assert.Contains("no user message", response);
    }

    [Fact]
    public async Task ChatCompletionAsync_EmptyMessages_ReturnsFallback()
    {
        var response = await _provider.ChatCompletionAsync([]);

        Assert.Contains("no user message", response);
    }

    // ── StructuredChatCompletionAsync ─────────────────────────────────────────

    [Fact]
    public async Task StructuredChatCompletionAsync_ReturnsNonNullOutput()
    {
        var messages = new[] { AiMessage.User("Describe the tavern.") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.NotNull(output);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_NarrationContainsUserContent()
    {
        var messages = new[] { AiMessage.User("Describe the tavern.") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.Contains("Describe the tavern.", output.Narration);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_NarrationIncludesFakePrefix()
    {
        var messages = new[] { AiMessage.User("Enter the dungeon.") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.StartsWith("[Fake AI]", output.Narration);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_PrivateDmNote_IsNull()
    {
        var messages = new[] { AiMessage.User("Any action.") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.Null(output.PrivateDmNote);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_RulesCitations_IsEmpty()
    {
        var messages = new[] { AiMessage.User("Any action.") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.Empty(output.RulesCitations);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_ProposedActions_IsEmpty()
    {
        var messages = new[] { AiMessage.User("Any action.") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.Empty(output.ProposedActions);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_NoUserMessage_NarrationContainsFallback()
    {
        var messages = new[] { AiMessage.System("System only") };

        var output = await _provider.StructuredChatCompletionAsync(messages);

        Assert.Contains("no user message", output.Narration);
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_SameInput_ReturnsSameResponse()
    {
        var messages = new[] { AiMessage.User("Stable input") };

        var first = await _provider.ChatCompletionAsync(messages);
        var second = await _provider.ChatCompletionAsync(messages);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_SameInput_ReturnsSameNarration()
    {
        var messages = new[] { AiMessage.User("Stable input") };

        var first = await _provider.StructuredChatCompletionAsync(messages);
        var second = await _provider.StructuredChatCompletionAsync(messages);

        Assert.Equal(first.Narration, second.Narration);
    }
}
