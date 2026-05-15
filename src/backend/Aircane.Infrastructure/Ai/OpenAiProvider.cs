using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// AI provider that calls the OpenAI chat completions API using raw <see cref="HttpClient"/> calls.
/// No OpenAI SDK is used - all serialization is handled by <c>System.Text.Json</c>.
/// <para>
/// Configuration keys:
/// <list type="bullet">
///   <item><c>Ai:OpenAi:ApiKey</c> - OpenAI API key (required; read from environment or user secrets)</item>
///   <item><c>Ai:OpenAi:Model</c> - model name (default: <c>gpt-4o-mini</c>)</item>
/// </list>
/// </para>
/// <para>
/// The API key is never logged. Use ASP.NET Core user secrets or environment variables to supply it.
/// </para>
/// </summary>
public sealed class OpenAiProvider : IAiProvider, IDisposable
{
    private const string ChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OpenAiProvider> _logger;

    /// <inheritdoc />
    public string ProviderName => "OpenAI";

    /// <summary>
    /// Initialises the provider using a pre-configured <see cref="HttpClient"/> from
    /// <see cref="IHttpClientFactory"/> and reads model/key settings from configuration.
    /// </summary>
    public OpenAiProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = configuration["Ai:OpenAi:Model"] ?? "gpt-4o-mini";

        var apiKey = configuration["Ai:OpenAi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI API key is not configured. " +
                "Set Ai:OpenAi:ApiKey via environment variables or ASP.NET Core user secrets.");

        // Set the Authorization header once on the shared client.
        // The key is never written to logs.
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <inheritdoc />
    public async Task<string> ChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var request = BuildRequest(messages, jsonMode: false);

        _logger.LogDebug(
            "Sending chat completion request to OpenAI. Model={Model}, MessageCount={Count}",
            _model, messages.Count);

        var response = await SendRequestAsync(request, ct);
        var text = ExtractContent(response);

        _logger.LogDebug("OpenAI chat completion received. ResponseLength={Length}", text.Length);

        return text;
    }

    /// <inheritdoc />
    public async Task<AiStructuredOutput> StructuredChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var request = BuildRequest(messages, jsonMode: true);

        _logger.LogDebug(
            "Sending structured chat completion request to OpenAI. Model={Model}, MessageCount={Count}",
            _model, messages.Count);

        var response = await SendRequestAsync(request, ct);
        var json = ExtractContent(response);

        _logger.LogDebug(
            "OpenAI structured completion received. ResponseLength={Length}", json.Length);

        try
        {
            var output = JsonSerializer.Deserialize<AiStructuredOutput>(json, JsonOptions);
            if (output is null)
                throw new InvalidOperationException(
                    "OpenAI returned a null structured output after deserialization.");

            return output;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"OpenAI returned a response that could not be deserialized as {nameof(AiStructuredOutput)}. " +
                "Ensure the system prompt instructs the model to return valid JSON matching the expected schema.",
                ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private OpenAiChatRequest BuildRequest(IReadOnlyList<AiMessage> messages, bool jsonMode)
    {
        var openAiMessages = messages
            .Select(m => new OpenAiMessage(m.Role, m.Content))
            .ToList();

        var responseFormat = jsonMode
            ? new OpenAiResponseFormat("json_object")
            : null;

        return new OpenAiChatRequest(
            Model: _model,
            Messages: openAiMessages,
            ResponseFormat: responseFormat);
    }

    private async Task<OpenAiChatResponse> SendRequestAsync(
        OpenAiChatRequest request,
        CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsEndpoint)
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Failed to reach the OpenAI API. Check your network connection.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The OpenAI API request timed out.", ex);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(httpResponse, ct);
            var statusCode = (int)httpResponse.StatusCode;

            throw httpResponse.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    new InvalidOperationException(
                        $"OpenAI API returned 401 Unauthorized. Verify that Ai:OpenAi:ApiKey is correct. {errorBody}"),
                HttpStatusCode.TooManyRequests =>
                    new InvalidOperationException(
                        $"OpenAI API returned 429 Too Many Requests. You may have exceeded your rate limit or quota. {errorBody}"),
                _ =>
                    new InvalidOperationException(
                        $"OpenAI API returned HTTP {statusCode}. {errorBody}"),
            };
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<OpenAiChatResponse>(
            JsonOptions, cancellationToken: ct);

        if (result is null)
            throw new InvalidOperationException("OpenAI API returned an empty response body.");

        return result;
    }

    private static string ExtractContent(OpenAiChatResponse response)
    {
        var choice = response.Choices?.FirstOrDefault();
        if (choice is null)
            throw new InvalidOperationException(
                "OpenAI API response contained no choices.");

        var content = choice.Message?.Content;
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException(
                "OpenAI API response choice contained no message content.");

        return content;
    }

    private static async Task<string> TryReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            // Truncate to avoid flooding logs with large error bodies.
            return body.Length > 500 ? body[..500] + "..." : body;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── OpenAI API DTOs ───────────────────────────────────────────────────────

    private sealed record OpenAiChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<OpenAiMessage> Messages,
        [property: JsonPropertyName("response_format")] OpenAiResponseFormat? ResponseFormat);

    private sealed record OpenAiMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OpenAiResponseFormat(
        [property: JsonPropertyName("type")] string Type);

    private sealed record OpenAiChatResponse(
        [property: JsonPropertyName("choices")] List<OpenAiChoice>? Choices);

    private sealed record OpenAiChoice(
        [property: JsonPropertyName("message")] OpenAiChoiceMessage? Message);

    private sealed record OpenAiChoiceMessage(
        [property: JsonPropertyName("content")] string? Content);
}
