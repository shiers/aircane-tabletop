using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.AiSettings;
using Aircane.Application.Retrieval;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Retrieval.Reranking;

/// <summary>
/// Builds the reranking provider from the live AI settings (<c>Reranking:Provider</c>: "None",
/// "Ollama" or "Cohere"; default "None"). Registered scoped, so a settings change takes effect on
/// the next request without a restart.
/// </summary>
public sealed class RerankingProviderFactory : IRerankingProviderFactory
{
    public const string ProviderKey = "Reranking:Provider";
    public const string OllamaModelKey = "Reranking:Ollama:Model";
    public const string CohereApiKeyKey = "Reranking:Cohere:ApiKey";
    public const string CohereModelKey = "Reranking:Cohere:Model";
    private const string OllamaBaseUrlKey = "Ai:Ollama:BaseUrl";

    private readonly IAiSettingsService _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public RerankingProviderFactory(
        IAiSettingsService settings,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IRerankingProvider CreateActive() => Build(
        _settings.GetRawSetting(ProviderKey),
        _settings.GetRawSetting(OllamaModelKey),
        _settings.GetRawSetting(CohereApiKeyKey),
        _settings.GetRawSetting(CohereModelKey));

    /// <inheritdoc />
    public IRerankingProvider Create(RerankingSettingsDto settings) => Build(
        settings.Provider,
        FirstNonEmpty(settings.Ollama?.Model, _settings.GetRawSetting(OllamaModelKey)),
        // A null or masked key (the UI echoes "****1234") means "use the saved key".
        IsMasked(settings.Cohere?.ApiKey) ? _settings.GetRawSetting(CohereApiKeyKey) : settings.Cohere?.ApiKey,
        FirstNonEmpty(settings.Cohere?.Model, _settings.GetRawSetting(CohereModelKey)));

    /// <summary>True for null, blank, or a masked key such as <c>************1234</c>.</summary>
    public static bool IsMasked(string? key) => string.IsNullOrWhiteSpace(key) || key.StartsWith("****", StringComparison.Ordinal);

    private IRerankingProvider Build(string? provider, string? ollamaModel, string? cohereKey, string? cohereModel)
    {
        switch (provider?.Trim().ToLowerInvariant())
        {
            case "ollama":
                return new OllamaRerankingProvider(
                    _httpClientFactory.CreateClient("Ollama"),
                    _settings.GetRawSetting(OllamaBaseUrlKey),
                    ollamaModel,
                    _loggerFactory.CreateLogger<OllamaRerankingProvider>());

            case "cohere":
                return new CohereRerankingProvider(
                    _httpClientFactory.CreateClient("Cohere"),
                    cohereKey,
                    cohereModel,
                    _loggerFactory.CreateLogger<CohereRerankingProvider>());

            default:
                return new NullRerankingProvider();
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
