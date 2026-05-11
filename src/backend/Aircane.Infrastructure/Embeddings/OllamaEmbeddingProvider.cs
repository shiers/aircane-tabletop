using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Embeddings;

/// <summary>
/// Embedding provider that calls a locally running Ollama instance.
/// <para>
/// Default model: <c>nomic-embed-text</c> (768 dimensions, fully local, no API key required).
/// </para>
/// <para>
/// Configuration keys:
/// <list type="bullet">
///   <item><c>Embeddings:Ollama:BaseUrl</c> — base URL of the Ollama server (default: <c>http://localhost:11434</c>)</item>
///   <item><c>Embeddings:Ollama:Model</c> — model name (default: <c>nomic-embed-text</c>)</item>
/// </list>
/// </para>
/// <para>
/// Integration testing requires a running Ollama instance with the configured model pulled.
/// Unit tests should use <see cref="FakeEmbeddingProvider"/> instead.
/// </para>
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;

    /// <inheritdoc />
    public int Dimensions => 768;

    /// <inheritdoc />
    public string ProviderName => "Ollama";

    public OllamaEmbeddingProvider(
        IConfiguration configuration,
        ILogger<OllamaEmbeddingProvider> logger)
    {
        _logger = logger;

        var baseUrl = configuration["Embeddings:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = configuration["Embeddings:Ollama:Model"] ?? "nomic-embed-text";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new OllamaEmbeddingRequest(Model: _model, Prompt: text);

        var response = await _httpClient.PostAsJsonAsync("api/embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
            cancellationToken: ct);

        if (result?.Embedding is null || result.Embedding.Length == 0)
            throw new InvalidOperationException(
                $"Ollama returned an empty embedding for model '{_model}'.");

        _logger.LogDebug(
            "Ollama embedding generated. Model={Model}, Dimensions={Dimensions}",
            _model, result.Embedding.Length);

        return result.Embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        // Ollama's /api/embeddings endpoint accepts one prompt at a time.
        // We call it sequentially to avoid overwhelming a local instance.
        var results = new List<float[]>(texts.Count);

        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await GenerateEmbeddingAsync(text, ct);
            results.Add(embedding);
        }

        return results;
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    // ── Ollama API DTOs ───────────────────────────────────────────────────────

    private sealed record OllamaEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
