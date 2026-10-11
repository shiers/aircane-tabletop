using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aircane.Application.Retrieval;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Retrieval.Reranking;

/// <summary>
/// Local reranking by asking an Ollama chat model to score each chunk's relevance to the question.
/// <para>
/// For every candidate the provider calls <c>POST /api/generate</c> with a prompt that asks for a single
/// relevance number from 0 to 10, parses the number out of the <c>response</c> field and normalises it to
/// 0..1. A candidate whose answer has no number scores 0.0. If <i>no</i> candidate yields a number, the
/// original order is kept and a warning is logged.
/// </para>
/// <para>
/// This is an LLM judge, not a trained cross-encoder: it needs no special model (any instruction-following
/// chat model installed in Ollama works, e.g. <c>llama3.2:3b</c>), but it is slower than a hosted reranker
/// (one call per candidate, a few in parallel) and its quality depends on the model. The dedicated
/// <c>bge-reranker-*</c> models are not in the Ollama library, and <c>/api/generate</c> cannot read a
/// classifier score from them.
/// </para>
/// Configuration: <c>Reranking:Ollama:Model</c> (default <c>llama3.2:3b</c>); the server URL is the
/// Ollama chat provider's <c>Ai:Ollama:BaseUrl</c>.
/// </summary>
public sealed partial class OllamaRerankingProvider : IRerankingProvider
{
    public const string Id = "Ollama";
    public const string DefaultModel = "llama3.2:3b";
    public const string DefaultBaseUrl = "http://localhost:11434";

    /// <summary>Candidates scored at the same time. Ollama queues requests beyond its own parallelism.</summary>
    private const int MaxConcurrentScoring = 4;

    /// <summary>The longest chunk text sent to the model, to bound prompt size.</summary>
    private const int MaxCandidateChars = 1500;

    private static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromSeconds(60);

    // Generous: the first call may have to load the model into memory.
    private static readonly TimeSpan AvailabilityProbeTimeout = TimeSpan.FromSeconds(30);

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
    /// True when Ollama is reachable and the configured model answers a test scoring prompt with a number.
    /// The result is cached for 60 seconds so the probe does not run on every request.
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

            // The interface exposes a synchronous property; the probe is cached and only slow while a
            // model is loading.
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
            using var gate = new SemaphoreSlim(MaxConcurrentScoring);
            var outcomes = await Task.WhenAll(candidates.Select(async candidate =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await GenerateScoreAsync(BuildPrompt(query, candidate.Text), ct);
                }
                finally
                {
                    gate.Release();
                }
            }));

            if (outcomes.Any(o => o.Status == HttpStatusCode.NotFound))
            {
                _logger.LogWarning(
                    "Ollama reranking model '{Model}' was not found at {BaseUrl}; keeping the original chunk order.",
                    _model, _baseUrl);
                AvailabilityCache[CacheKey] = (DateTime.UtcNow, false);
                return NullRerankingProvider.PreserveOrder(candidates);
            }

            if (outcomes.All(o => o.Score is null))
            {
                _logger.LogWarning(
                    "Ollama model '{Model}' returned no parseable relevance score for any of {Count} candidates; " +
                    "keeping the original chunk order.",
                    _model, candidates.Count);
                return NullRerankingProvider.PreserveOrder(candidates);
            }

            // A candidate with no parseable answer scores 0.0 rather than being dropped.
            return candidates
                .Select((c, i) => new RerankResult(c.Id, outcomes[i].Score ?? 0f))
                .OrderByDescending(r => r.Score)
                .ToList();
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
        cts.CancelAfter(AvailabilityProbeTimeout);

        var result = await ProbeAsync(cts.Token);
        AvailabilityCache[CacheKey] = (DateTime.UtcNow, result.Available);
        return result;
    }

    /// <summary>The scoring prompt sent to the model for one query/chunk pair.</summary>
    internal static string BuildPrompt(string query, string chunkText)
    {
        var text = chunkText.Length <= MaxCandidateChars ? chunkText : chunkText[..MaxCandidateChars];

        return "You are a relevance judge for a tabletop RPG rules search.\n" +
               "Rate how well the PASSAGE answers the QUESTION on a scale from 0 (irrelevant) to 10 " +
               "(directly answers it).\n" +
               "Reply with only the number, nothing else.\n\n" +
               $"QUESTION: {query}\n\n" +
               $"PASSAGE: {text}\n\n" +
               "RELEVANCE (0-10):";
    }

    /// <summary>
    /// Extracts a relevance score from model output and normalises it to 0..1 (the model is asked for
    /// 0-10). Reasoning blocks (<c>&lt;think&gt;...&lt;/think&gt;</c>) are ignored. Returns null when the
    /// text contains no number.
    /// </summary>
    internal static float? ParseScore(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var cleaned = ThinkBlock().Replace(response, string.Empty);
        var match = Number().Match(cleaned);
        if (!match.Success
            || !float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || float.IsNaN(value))
        {
            return null;
        }

        return Math.Clamp(value, 0f, 10f) / 10f;
    }

    /// <summary>Clears the shared availability cache (for tests).</summary>
    internal static void ClearAvailabilityCache() => AvailabilityCache.Clear();

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"-?\d+(?:\.\d+)?")]
    private static partial Regex Number();

    private string CacheKey => $"{_baseUrl}|{_model}";

    private async Task<RerankingConnectionResult> ProbeAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await GenerateScoreAsync(
                BuildPrompt("How many hit points does a goblin have?", "A goblin has 7 (2d6) hit points."), ct);

            if (outcome.Status == HttpStatusCode.NotFound)
                return new RerankingConnectionResult(false,
                    $"Model '{_model}' was not found on the Ollama server at {_baseUrl}. Run: ollama pull {_model}");

            if (outcome.Status != HttpStatusCode.OK)
                return new RerankingConnectionResult(false,
                    $"Ollama at {_baseUrl} returned HTTP {(int)outcome.Status} for model '{_model}'.");

            if (outcome.Score is null)
                return new RerankingConnectionResult(false,
                    $"Model '{_model}' did not reply with a relevance number. Choose an instruction-following chat model.");

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

    /// <summary>Calls <c>/api/generate</c> once and returns the HTTP status and the parsed score, if any.</summary>
    private async Task<(HttpStatusCode Status, float? Score)> GenerateScoreAsync(string prompt, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            prompt,
            stream = false,
            options = new { temperature = 0, num_predict = 16 },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, null);

        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() : null;
            return (response.StatusCode, ParseScore(text));
        }
        catch (JsonException)
        {
            return (response.StatusCode, null);
        }
    }
}
