using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aircane.Application.Retrieval;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Retrieval.Reranking;

/// <summary>
/// Reranking via the Cohere Rerank API (<c>POST https://api.cohere.com/v1/rerank</c>).
/// Configuration: <c>Reranking:Cohere:ApiKey</c> and <c>Reranking:Cohere:Model</c> (default
/// <c>rerank-english-v3.0</c>). Never throws from <see cref="RerankAsync"/>: any failure (401, 429,
/// network, bad payload) is logged and the candidates are returned in their original order.
/// </summary>
public sealed class CohereRerankingProvider : IRerankingProvider
{
    public const string Id = "Cohere";
    public const string DefaultModel = "rerank-english-v3.0";
    public const string Endpoint = "https://api.cohere.com/v1/rerank";

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ILogger<CohereRerankingProvider> _logger;

    public CohereRerankingProvider(
        HttpClient httpClient,
        string? apiKey,
        string? model,
        ILogger<CohereRerankingProvider> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return [];

        if (!IsAvailable)
        {
            _logger.LogWarning("Cohere reranking skipped: no API key is configured.");
            return NullRerankingProvider.PreserveOrder(candidates);
        }

        try
        {
            using var response = await SendAsync(query, candidates, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Cohere API key is invalid; keeping the original chunk order.");
                return NullRerankingProvider.PreserveOrder(candidates);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Cohere rate limit hit; keeping the original chunk order.");
                return NullRerankingProvider.PreserveOrder(candidates);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cohere rerank returned HTTP {Status}; keeping the original chunk order.",
                    (int)response.StatusCode);
                return NullRerankingProvider.PreserveOrder(candidates);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return MapResponse(json, candidates);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cohere reranking failed; keeping the original chunk order.");
            return NullRerankingProvider.PreserveOrder(candidates);
        }
    }

    /// <inheritdoc />
    public async Task<RerankingConnectionResult> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new RerankingConnectionResult(false, "A Cohere API key is required.");

        try
        {
            // A real one-document call, so a bad key or model is detected (RerankAsync would hide it).
            using var response = await SendAsync(
                "test", [new RerankCandidate("test", "test document")], ct);

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new RerankingConnectionResult(false, "Cohere API key is invalid."),
                HttpStatusCode.TooManyRequests => new RerankingConnectionResult(false, "Cohere rate limit hit. Try again shortly."),
                _ when response.IsSuccessStatusCode => new RerankingConnectionResult(true, $"Connected to Cohere (model {_model})."),
                _ => new RerankingConnectionResult(false, $"Cohere returned HTTP {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RerankingConnectionResult(false, $"Could not reach Cohere: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string query, IReadOnlyList<RerankCandidate> candidates, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            query,
            documents = candidates.Select(c => c.Text).ToArray(),
            top_n = candidates.Count,
            return_documents = false,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Maps <c>results[].index</c> / <c>results[].relevance_score</c> back to candidate ids, in
    /// descending score order. Candidates the API did not return are appended in their original order
    /// below every returned one, so the result always covers every candidate exactly once.
    /// </summary>
    internal static IReadOnlyList<RerankResult> MapResponse(string json, IReadOnlyList<RerankCandidate> candidates)
    {
        using var doc = JsonDocument.Parse(json);
        var scored = new Dictionary<int, float>();

        foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var index = item.GetProperty("index").GetInt32();
            if (index >= 0 && index < candidates.Count)
                scored[index] = item.GetProperty("relevance_score").GetSingle();
        }

        var results = scored
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new RerankResult(candidates[kv.Key].Id, kv.Value))
            .ToList();

        var floor = results.Count > 0 ? results.Min(r => r.Score) : 0f;
        var missing = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!scored.ContainsKey(i))
                results.Add(new RerankResult(candidates[i].Id, floor - 0.001f * ++missing));
        }

        return results;
    }
}
