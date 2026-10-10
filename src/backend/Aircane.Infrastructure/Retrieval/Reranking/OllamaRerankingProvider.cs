using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Aircane.Application.Retrieval;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Retrieval.Reranking;

/// <summary>
/// Reranking via a cross-encoder model served by Ollama, following the scheme in the feature
/// request: each candidate is sent as <c>[CLS] query [SEP] chunk [SEP]</c> to <c>POST /api/embed</c>
/// and the first value of the returned vector is used as its relevance score.
/// <para>
/// <b>Caveat:</b> Ollama has no rerank endpoint and, as of 0.34.4, <c>bge-reranker-base</c> is not in
/// its model library (<c>ollama pull bge-reranker-base</c> fails). <c>/api/embed</c> also returns a
/// pooled embedding, not a classifier score, so the "first value" is only meaningful if a model that
/// was converted to expose a score there is installed. When the model is missing this provider reports
/// itself unavailable and <see cref="RerankAsync"/> keeps the original order. Prefer Cohere unless you
/// have verified a local model produces meaningful scores.
/// </para>
/// Configuration: <c>Reranking:Ollama:Model</c> (default <c>bge-reranker-base</c>); the server URL is
/// the Ollama chat provider's <c>Ai:Ollama:BaseUrl</c>.
/// </summary>
public sealed class OllamaRerankingProvider : IRerankingProvider
{
    public const string Id = "Ollama";
    public const string DefaultModel = "bge-reranker-base";
    public const string DefaultBaseUrl = "http://localhost:11434";

    private static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AvailabilityProbeTimeout = TimeSpan.FromSeconds(3);

    // Provider instances are created per request, so the availability cache is shared across them.
    private static readonly ConcurrentDictionary<string, (DateTime CheckedAtUtc, bool Available)> AvailabilityCache = new();

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly ILogger<OllamaRerankingProvider> _logger;

    public OllamaRerankingProvider(
        HttpClient httpClient,
        string? baseUrl,
        string? model,
        ILogger<OllamaRerankingProvider> logger)
    {
        _httpClient = httpClient;
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl).TrimEnd('/');
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <summary>
    /// True when Ollama is reachable and the model answers a test embed call. The result is cached
    /// for 60 seconds so the probe does not run on every request.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            var key = CacheKey;
            if (AvailabilityCache.TryGetValue(key, out var cached)
                && DateTime.UtcNow - cached.CheckedAtUtc < AvailabilityCacheDuration)
            {
                return cached.Available;
            }

            // The interface exposes a synchronous property; the probe is short (3s cap) and cached.
            using var cts = new CancellationTokenSource(AvailabilityProbeTimeout);
            var result = ProbeAsync(cts.Token).GetAwaiter().GetResult();
            AvailabilityCache[key] = (DateTime.UtcNow, result.Available);
            return result.Available;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return [];

        try
        {
            var scores = new List<RerankResult>(candidates.Count);

            foreach (var candidate in candidates)
            {
                var (status, score) = await EmbedFirstValueAsync(BuildInput(query, candidate.Text), ct);

                if (status == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "Ollama reranking model '{Model}' was not found at {BaseUrl}; keeping the original chunk order.",
                        _model, _baseUrl);
                    AvailabilityCache[CacheKey] = (DateTime.UtcNow, false);
                    return NullRerankingProvider.PreserveOrder(candidates);
                }

                if (score is null)
                {
                    _logger.LogWarning(
                        "Ollama reranking returned HTTP {Status} / no score; keeping the original chunk order.",
                        (int)status);
                    return NullRerankingProvider.PreserveOrder(candidates);
                }

                scores.Add(new RerankResult(candidate.Id, score.Value));
            }

            return scores.OrderByDescending(r => r.Score).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama reranking failed; keeping the original chunk order.");
            return NullRerankingProvider.PreserveOrder(candidates);
        }
    }

    /// <inheritdoc />
    public async Task<RerankingConnectionResult> CheckConnectionAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var result = await ProbeAsync(cts.Token);
        AvailabilityCache[CacheKey] = (DateTime.UtcNow, result.Available);
        return result;
    }

    /// <summary>The cross-encoder input format: <c>[CLS] query [SEP] chunk [SEP]</c>.</summary>
    internal static string BuildInput(string query, string chunkText) => $"[CLS] {query} [SEP] {chunkText} [SEP]";

    /// <summary>Clears the shared availability cache (for tests).</summary>
    internal static void ClearAvailabilityCache() => AvailabilityCache.Clear();

    private string CacheKey => $"{_baseUrl}|{_model}";

    private async Task<RerankingConnectionResult> ProbeAsync(CancellationToken ct)
    {
        try
        {
            var (status, score) = await EmbedFirstValueAsync(BuildInput("test", "test"), ct);

            if (status == HttpStatusCode.NotFound)
                return new RerankingConnectionResult(false,
                    $"Model '{_model}' was not found on the Ollama server at {_baseUrl}.");

            if (score is null)
                return new RerankingConnectionResult(false,
                    $"Ollama at {_baseUrl} returned HTTP {(int)status} for model '{_model}'.");

            return new RerankingConnectionResult(true, $"Ollama reranker is running (model {_model}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new RerankingConnectionResult(false, $"Timed out waiting for Ollama at {_baseUrl}.");
        }
        catch (Exception ex)
        {
            return new RerankingConnectionResult(false, $"Ollama is not reachable at {_baseUrl}: {ex.Message}");
        }
    }

    /// <summary>Calls <c>/api/embed</c> and returns the status plus the first value of the first embedding, if any.</summary>
    private async Task<(HttpStatusCode Status, float? Score)> EmbedFirstValueAsync(string input, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { model = _model, input });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/embed")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, null);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("embeddings", out var embeddings)
            && embeddings.GetArrayLength() > 0
            && embeddings[0].GetArrayLength() > 0)
        {
            return (response.StatusCode, embeddings[0][0].GetSingle());
        }

        return (response.StatusCode, null);
    }
}
