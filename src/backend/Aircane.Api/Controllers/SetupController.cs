using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.AiSettings;
using Aircane.Application.DTOs.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// First-launch / setup status for the setup wizard.
/// Anonymous by design: the wizard runs before any session or participant token exists,
/// and this endpoint returns only booleans and provider display names — no secrets.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class SetupController : ControllerBase
{
    private readonly IAiSettingsService _settingsService;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SetupController> _logger;

    public SetupController(
        IAiSettingsService settingsService,
        IEmbeddingProvider embeddingProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<SetupController> logger)
    {
        _settingsService = settingsService;
        _embeddingProvider = embeddingProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns first-launch detection and AI-configuration status for the setup wizard.
    /// </summary>
    [HttpGet("api/setup/status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SetupStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SetupStatusDto>> GetStatus(CancellationToken ct)
    {
        var config = _settingsService.GetCurrentConfig();
        var activeProvider = config.ActiveProvider;

        var isFirstLaunch = !_settingsService.SettingsFileExists();
        var aiConfigured = activeProvider != AiProviderType.Fake;

        var ollamaReachable = await CheckOllamaReachableAsync(ct);

        var dto = new SetupStatusDto(
            IsFirstLaunch: isFirstLaunch,
            AiProviderConfigured: aiConfigured,
            ActiveAiProvider: DisplayName(activeProvider),
            ActiveEmbeddingProvider: DisplayEmbeddingName(_embeddingProvider.ProviderName),
            OllamaReachable: ollamaReachable);

        return Ok(dto);
    }

    /// <summary>
    /// Quick reachability probe of the configured Ollama daemon. Uses a short timeout so
    /// the setup wizard's live status poll stays snappy. Never throws — returns false on
    /// any failure (not installed, not running, timeout).
    /// </summary>
    private async Task<bool> CheckOllamaReachableAsync(CancellationToken ct)
    {
        var baseUrl = (_settingsService.GetRawSetting("Ai:Ollama:BaseUrl") ?? "http://localhost:11434")
            .TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("Ollama");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Short timeout: the daemon is local, so it answers fast when up. A missing
            // daemon should fail quickly rather than stalling the wizard's poll.
            cts.CancelAfter(TimeSpan.FromMilliseconds(750));

            var response = await client.GetAsync($"{baseUrl}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama reachability probe failed for {BaseUrl}", baseUrl);
            return false;
        }
    }

    /// <summary>Maps the chat provider enum to a friendly display name for the UI.</summary>
    private static string DisplayName(AiProviderType provider) => provider switch
    {
        AiProviderType.Fake => "Fake",
        AiProviderType.OpenAi => "OpenAI",
        AiProviderType.AzureOpenAi => "Azure OpenAI",
        AiProviderType.AwsBedrock => "AWS Bedrock",
        AiProviderType.Ollama => "Ollama",
        AiProviderType.Grok => "Grok",
        _ => provider.ToString(),
    };

    /// <summary>Normalises the embedding provider name to a friendly display name.</summary>
    private static string DisplayEmbeddingName(string providerName) =>
        string.Equals(providerName, "OpenAi", StringComparison.OrdinalIgnoreCase) ? "OpenAI"
        : providerName;
}
