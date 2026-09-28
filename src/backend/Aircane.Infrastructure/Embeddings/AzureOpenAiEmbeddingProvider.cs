using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Embeddings;

/// <summary>
/// Embedding provider that calls an Azure OpenAI embeddings deployment using raw
/// <see cref="HttpClient"/> calls. Follows the same pattern as
/// <see cref="OpenAiEmbeddingProvider"/> but uses the Azure endpoint + deployment URL scheme and
/// the <c>api-key</c> header.
/// </summary>
/// <remarks>
/// Configuration keys:
/// <list type="bullet">
///   <item><c>Ai:AzureOpenAi:Endpoint</c> — resource endpoint (e.g. https://my-resource.openai.azure.com).</item>
///   <item><c>Ai:AzureOpenAi:ApiKey</c> — resource key (never logged).</item>
///   <item><c>Ai:AzureOpenAi:ApiVersion</c> — API version (default 2024-02-01).</item>
///   <item><c>Embeddings:AzureOpenAi:DeploymentName</c> — the embedding deployment name.</item>
///   <item><c>Embeddings:AzureOpenAi:Dimensions</c> — reported dimension (default 1536).</item>
/// </list>
/// </remarks>
public sealed class AzureOpenAiEmbeddingProvider : IEmbeddingProvider
{
    public const int DefaultDimensions = 1536;

    private readonly HttpClient _httpClient;
    private readonly string _requestUri;
    private readonly string _deploymentName;
    private readonly ILogger<AzureOpenAiEmbeddingProvider> _logger;

    public int Dimensions { get; }
    public string ProviderName => "AzureOpenAI";
    public string ModelName => _deploymentName;

    public AzureOpenAiEmbeddingProvider(
        HttpClient httpClient,
        string? endpoint,
        string? apiKey,
        string? deploymentName,
        string? apiVersion,
        int? dimensions,
        ILogger<AzureOpenAiEmbeddingProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _deploymentName = deploymentName ?? string.Empty;
        Dimensions = dimensions is > 0 ? dimensions.Value : DefaultDimensions;

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint is not configured.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Azure OpenAI API key is not configured.");
        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new InvalidOperationException("Azure OpenAI embedding deployment name is not configured.");

        var version = string.IsNullOrWhiteSpace(apiVersion) ? "2024-02-01" : apiVersion;
        _requestUri =
            $"{endpoint.TrimEnd('/')}/openai/deployments/{deploymentName}/embeddings?api-version={version}";

        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
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

        var request = new AzureEmbeddingRequest(Input: texts.ToArray());

        var response = await _httpClient.PostAsJsonAsync(_requestUri, request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var truncated = body.Length > 500 ? body[..500] : body;
            throw new InvalidOperationException(
                $"Azure OpenAI embeddings request failed ({(int)response.StatusCode} {response.StatusCode}): {truncated}");
        }

        var result = await response.Content.ReadFromJsonAsync<AzureEmbeddingResponse>(cancellationToken: ct);
        if (result?.Data is null || result.Data.Count == 0)
            throw new InvalidOperationException(
                $"Azure OpenAI returned no embeddings for deployment '{_deploymentName}'.");

        var ordered = result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();

        _logger.LogDebug(
            "Azure OpenAI embeddings generated. Deployment={Deployment}, Count={Count}, Dimensions={Dim}",
            _deploymentName, ordered.Count, ordered.Count > 0 ? ordered[0].Length : 0);

        return ordered;
    }

    // ── Azure OpenAI API DTOs ────────────────────────────────────────────────────

    private sealed record AzureEmbeddingRequest(
        [property: JsonPropertyName("input")] string[] Input);

    private sealed record AzureEmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<AzureEmbeddingData> Data);

    private sealed record AzureEmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
