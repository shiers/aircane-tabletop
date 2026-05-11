using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="OpenAiProvider"/>.
/// Uses a stub <see cref="HttpMessageHandler"/> to intercept HTTP calls without
/// making real network requests.
/// </summary>
public class OpenAiProviderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal IConfiguration containing the required OpenAI settings.
    /// The API key is a placeholder — no real key is used in tests.
    /// </summary>
    private static IConfiguration BuildConfig(
        string apiKey = "test-api-key-placeholder",
        string model = "gpt-4o-mini")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:OpenAi:ApiKey"] = apiKey,
                ["Ai:OpenAi:Model"] = model,
            })
            .Build();
    }

    /// <summary>
    /// Builds an <see cref="OpenAiProvider"/> whose HTTP calls are intercepted by
    /// <paramref name="handler"/>.
    /// </summary>
    private static OpenAiProvider BuildProvider(
        HttpMessageHandler handler,
        IConfiguration? config = null)
    {
        var httpClient = new HttpClient(handler);
        return new OpenAiProvider(
            httpClient,
            config ?? BuildConfig(),
            NullLogger<OpenAiProvider>.Instance);
    }

    /// <summary>
    /// Creates a stub handler that returns a successful OpenAI-shaped JSON response
    /// with the given <paramref name="content"/> as the message content.
    /// </summary>
    private static CapturingHandler SuccessHandler(string content)
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content } }
            }
        });

        return new CapturingHandler(HttpStatusCode.OK, body);
    }

    // ── Provider metadata ─────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_ReturnsOpenAI()
    {
        using var provider = BuildProvider(SuccessHandler("hello"));

        Assert.Equal("OpenAI", provider.ProviderName);
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_MissingApiKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:OpenAi:ApiKey"] = null,
                ["Ai:OpenAi:Model"] = "gpt-4o-mini",
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildProvider(SuccessHandler("x"), config));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Constructor_EmptyApiKey_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(apiKey: "   ");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildProvider(SuccessHandler("x"), config));

        Assert.Contains("API key", ex.Message);
    }

    // ── ChatCompletionAsync — request format ──────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_SendsPostToCorrectEndpoint()
    {
        var handler = SuccessHandler("The goblin has AC 15.");
        using var provider = BuildProvider(handler);

        await provider.ChatCompletionAsync([AiMessage.User("What is goblin AC?")]);

        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal(
            "https://api.openai.com/v1/chat/completions",
            handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task ChatCompletionAsync_SendsAuthorizationHeader()
    {
        var handler = SuccessHandler("ok");
        using var provider = BuildProvider(handler, BuildConfig(apiKey: "sk-test-key"));

        await provider.ChatCompletionAsync([AiMessage.User("Hello")]);

        var auth = handler.LastRequest?.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("sk-test-key", auth.Parameter);
    }

    [Fact]
    public async Task ChatCompletionAsync_SendsConfiguredModel()
    {
        var handler = SuccessHandler("ok");
        using var provider = BuildProvider(handler, BuildConfig(model: "gpt-4o"));

        await provider.ChatCompletionAsync([AiMessage.User("Hello")]);

        var body = await handler.LastRequestBodyAsync();
        Assert.Contains("gpt-4o", body);
    }

    [Fact]
    public async Task ChatCompletionAsync_SendsAllMessages()
    {
        var handler = SuccessHandler("ok");
        using var provider = BuildProvider(handler);

        var messages = new[]
        {
            AiMessage.System("You are a DM."),
            AiMessage.User("What is the AC of a goblin?"),
        };

        await provider.ChatCompletionAsync(messages);

        var body = await handler.LastRequestBodyAsync();
        Assert.Contains("system", body);
        Assert.Contains("You are a DM.", body);
        Assert.Contains("user", body);
        Assert.Contains("What is the AC of a goblin?", body);
    }

    [Fact]
    public async Task ChatCompletionAsync_DoesNotSendResponseFormat()
    {
        // For plain chat completions, response_format should not be set.
        var handler = SuccessHandler("ok");
        using var provider = BuildProvider(handler);

        await provider.ChatCompletionAsync([AiMessage.User("Hello")]);

        var body = await handler.LastRequestBodyAsync();
        Assert.DoesNotContain("json_object", body);
    }

    // ── ChatCompletionAsync — response handling ───────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_ReturnsMessageContent()
    {
        var handler = SuccessHandler("The goblin has AC 15.");
        using var provider = BuildProvider(handler);

        var result = await provider.ChatCompletionAsync([AiMessage.User("Goblin AC?")]);

        Assert.Equal("The goblin has AC 15.", result);
    }

    [Fact]
    public async Task ChatCompletionAsync_EmptyChoices_ThrowsInvalidOperationException()
    {
        var body = JsonSerializer.Serialize(new { choices = Array.Empty<object>() });
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        using var provider = BuildProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ChatCompletionAsync([AiMessage.User("Hello")]));
    }

    [Fact]
    public async Task ChatCompletionAsync_NullContent_ThrowsInvalidOperationException()
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = (string?)null } } }
        });
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        using var provider = BuildProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ChatCompletionAsync([AiMessage.User("Hello")]));
    }

    // ── ChatCompletionAsync — API error handling ──────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_401Response_ThrowsWithUnauthorizedMessage()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_api_key\"}");
        using var provider = BuildProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ChatCompletionAsync([AiMessage.User("Hello")]));

        Assert.Contains("401", ex.Message);
        Assert.Contains("Unauthorized", ex.Message);
    }

    [Fact]
    public async Task ChatCompletionAsync_429Response_ThrowsWithRateLimitMessage()
    {
        var handler = new CapturingHandler(HttpStatusCode.TooManyRequests, "{\"error\":\"rate_limit_exceeded\"}");
        using var provider = BuildProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ChatCompletionAsync([AiMessage.User("Hello")]));

        Assert.Contains("429", ex.Message);
        Assert.Contains("Too Many Requests", ex.Message);
    }

    [Fact]
    public async Task ChatCompletionAsync_500Response_ThrowsWithStatusCode()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "{\"error\":\"server_error\"}");
        using var provider = BuildProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ChatCompletionAsync([AiMessage.User("Hello")]));

        Assert.Contains("500", ex.Message);
    }

    // ── StructuredChatCompletionAsync — request format ────────────────────────

    [Fact]
    public async Task StructuredChatCompletionAsync_SendsJsonModeResponseFormat()
    {
        var structuredJson = BuildStructuredJson("You enter the dungeon.");
        var handler = SuccessHandler(structuredJson);
        using var provider = BuildProvider(handler);

        await provider.StructuredChatCompletionAsync([AiMessage.User("Describe the scene.")]);

        var body = await handler.LastRequestBodyAsync();
        Assert.Contains("json_object", body);
        Assert.Contains("response_format", body);
    }

    // ── StructuredChatCompletionAsync — response handling ────────────────────

    [Fact]
    public async Task StructuredChatCompletionAsync_DeserializesNarration()
    {
        var structuredJson = BuildStructuredJson("The door creaks open.");
        var handler = SuccessHandler(structuredJson);
        using var provider = BuildProvider(handler);

        var output = await provider.StructuredChatCompletionAsync([AiMessage.User("Enter the room.")]);

        Assert.Equal("The door creaks open.", output.Narration);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_DeserializesPrivateDmNote()
    {
        var json = JsonSerializer.Serialize(new
        {
            narration = "The rogue slips past.",
            private_dm_note = "The guard noticed but will not act until next round.",
            rules_citations = Array.Empty<object>(),
            proposed_actions = Array.Empty<object>(),
        });
        var handler = SuccessHandler(json);
        using var provider = BuildProvider(handler);

        var output = await provider.StructuredChatCompletionAsync([AiMessage.User("Sneak past.")]);

        Assert.Equal("The guard noticed but will not act until next round.", output.PrivateDmNote);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        var handler = SuccessHandler("this is not valid json {{{");
        using var provider = BuildProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.StructuredChatCompletionAsync([AiMessage.User("Hello")]));

        Assert.Contains(nameof(AiStructuredOutput), ex.Message);
    }

    [Fact]
    public async Task StructuredChatCompletionAsync_EmptyChoices_ThrowsInvalidOperationException()
    {
        var body = JsonSerializer.Serialize(new { choices = Array.Empty<object>() });
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        using var provider = BuildProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.StructuredChatCompletionAsync([AiMessage.User("Hello")]));
    }

    // ── StructuredChatCompletionAsync — API error handling ───────────────────

    [Fact]
    public async Task StructuredChatCompletionAsync_401Response_ThrowsWithUnauthorizedMessage()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_api_key\"}");
        using var provider = BuildProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.StructuredChatCompletionAsync([AiMessage.User("Hello")]));

        Assert.Contains("401", ex.Message);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal valid <see cref="AiStructuredOutput"/> JSON string.
    /// </summary>
    private static string BuildStructuredJson(string narration) =>
        JsonSerializer.Serialize(new
        {
            narration,
            private_dm_note = (string?)null,
            rules_citations = Array.Empty<object>(),
            proposed_actions = Array.Empty<object>(),
        });
}

// ── Test infrastructure ───────────────────────────────────────────────────────

/// <summary>
/// A stub <see cref="HttpMessageHandler"/> that returns a pre-configured response
/// and captures the last outgoing request for assertion.
/// </summary>
internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    private HttpRequestMessage? _lastRequest;
    private string? _lastRequestBody;

    public HttpRequestMessage? LastRequest => _lastRequest;

    public CapturingHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _lastRequest = request;

        if (request.Content is not null)
            _lastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Returns the body of the last captured request, or empty string if none.</summary>
    public Task<string> LastRequestBodyAsync() =>
        Task.FromResult(_lastRequestBody ?? string.Empty);
}
