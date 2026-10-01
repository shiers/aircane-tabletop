using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// AI provider that calls a locally running Ollama instance for chat completions.
/// Uses Ollama's OpenAI-compatible endpoint (/v1/chat/completions) for consistency.
/// <para>
/// No API key required. Ollama runs fully offline on the host machine.
/// This is the recommended default provider for live TTRPG play due to low latency.
/// </para>
/// </summary>
public sealed class OllamaProvider : IAiProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaProvider> _logger;

    /// <inheritdoc />
    public string ProviderName => "Ollama";

    public OllamaProvider(
        HttpClient httpClient,
        string baseUrl,
        string? model,
        ILogger<OllamaProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = model ?? "llama3.1:8b";

        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    /// <inheritdoc />
    public async Task<string> ChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var request = BuildRequest(messages, jsonMode: false);

        _logger.LogDebug(
            "Sending chat completion to Ollama. Model={Model}, MessageCount={Count}",
            _model, messages.Count);

        var response = await SendRequestAsync(request, ct);
        var text = ExtractContent(response);

        _logger.LogDebug("Ollama chat completion received. ResponseLength={Length}", text.Length);

        return text;
    }

    /// <inheritdoc />
    public async Task<AiStructuredOutput> StructuredChatCompletionAsync(
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var request = BuildRequest(messages, jsonMode: true);

        _logger.LogDebug(
            "Sending structured chat completion to Ollama. Model={Model}, MessageCount={Count}",
            _model, messages.Count);

        var response = await SendRequestAsync(request, ct);
        var json = ExtractContent(response);

        _logger.LogDebug("Ollama structured completion received. ResponseLength={Length}", json.Length);

        try
        {
            var output = JsonSerializer.Deserialize<AiStructuredOutput>(json, JsonOptions);
            if (output is null)
                throw new InvalidOperationException(
                    "Ollama returned a null structured output after deserialization.");

            return output;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Ollama returned a response that could not be deserialized as {nameof(AiStructuredOutput)}. " +
                "Ensure the system prompt instructs the model to return valid JSON matching the expected schema.",
                ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private OllamaChatRequest BuildRequest(IReadOnlyList<AiMessage> messages, bool jsonMode)
    {
        var ollamaMessages = messages
            .Select(m => new OllamaMessage(m.Role, m.Content))
            .ToList();

        return new OllamaChatRequest(
            Model: _model,
            Messages: ollamaMessages,
            Stream: false,
            Format: jsonMode ? "json" : null);
    }

    private async Task<OllamaChatResponse> SendRequestAsync(
        OllamaChatRequest request,
        CancellationToken ct)
    {
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.PostAsJsonAsync("api/chat", request, JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Failed to reach Ollama. Is it running? Start it with 'ollama serve'.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Ollama request timed out. The model may still be loading.", ex);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await TryReadErrorBodyAsync(httpResponse, ct);
            throw new InvalidOperationException(
                $"Ollama returned HTTP {(int)httpResponse.StatusCode}. {errorBody}");
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<OllamaChatResponse>(
            JsonOptions, cancellationToken: ct);

        if (result is null)
            throw new InvalidOperationException("Ollama returned an empty response body.");

        return result;
    }

    private static string ExtractContent(OllamaChatResponse response)
    {
        var content = response.Message?.Content;
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException(
                "Ollama response contained no message content.");

        return content;
    }

    private static async Task<string> TryReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length > 500 ? body[..500] + "..." : body;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── Ollama API DTOs ───────────────────────────────────────────────────────

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<OllamaMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] string? Format);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaResponseMessage? Message);

    private sealed record OllamaResponseMessage(
        [property: JsonPropertyName("content")] string? Content);
}
