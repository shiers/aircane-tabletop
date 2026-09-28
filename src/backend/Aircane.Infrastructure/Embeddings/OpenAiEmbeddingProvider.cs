using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Embeddings;

/// <summary>
/// Embedding provider that calls the OpenAI embeddings API using raw <see cref="HttpClient"/>
/// calls (no SDK). Defaults to <c>text-embedding-3-small</c> (1536 dimensions).
/// </summary>
/// <remarks>
/// Configuration keys:
/// <list type="bullet">
///   <item><c>Embeddings:OpenAi:Model</c> — embedding model (default <c>text-embedding-3-small</c>).</item>
///   <item><c>Embeddings:OpenAi:Dimensions</c> — reported dimension (default 1536; must match the model).</item>
/// </list>
/// The API key is supplied by the caller (reused from the chat provider's <c>Ai:OpenAi:ApiKey</c>)
/// and is never logged.
/// </remarks>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private const string EmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";

    /// <summary>Default OpenAI embedding model and its native dimension.</summary>
    public const string DefaultModel = "text-embedding-3-small";
    public const int DefaultDimensions = 1536;

    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;

    public int Dimensions { get; }
    public string ProviderName => "OpenAI";
    public string ModelName => _model;

    public OpenAiEmbeddingProvider(
        HttpClient httpClient,
        string? apiKey,
        string? model,
        int? dimensions,
        ILogger<OpenAiEmbeddingProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        Dimensions = dimensions is > 0 ? dimensions.Value : DefaultDimensions;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI API key is not configured. Set it via AI Provider Settings or environment variables.");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var results = await GenerateEmbeddingsAsync([text], ct);
        return results[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return [];

        var request = new OpenAiEmbeddingRequest(Model: _model, Input: texts.ToArray());

        var response = await _httpClient.PostAsJsonAsync(EmbeddingsEndpoint, request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var truncated = body.Length > 500 ? body[..500] : body;
            throw new InvalidOperationException(
                $"OpenAI embeddings request failed ({(int)response.StatusCode} {response.StatusCode}): {truncated}");
        }

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: ct);
        if (result?.Data is null || result.Data.Count == 0)
            throw new InvalidOperationException($"OpenAI returned no embeddings for model '{_model}'.");

        // Preserve request order (OpenAI returns an index per item).
        var ordered = result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();

        _logger.LogDebug(
            "OpenAI embeddings generated. Model={Model}, Count={Count}, Dimensions={Dim}",
            _model, ordered.Count, ordered.Count > 0 ? ordered[0].Length : 0);

        return ordered;
    }

    // ── OpenAI API DTOs ─────────────────────────────────────────────────────────

    private sealed record OpenAiEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input);

    private sealed record OpenAiEmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<OpenAiEmbeddingData> Data);

    private sealed record OpenAiEmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
