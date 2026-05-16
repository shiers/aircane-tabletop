using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.AiSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Manages AI provider configuration.
/// The host can select the active provider, configure credentials, and test connectivity.
/// Keys are never exposed unmasked in API responses.
/// </summary>
[ApiController]
[Route("api/ai/settings")]
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public class AiSettingsController : ControllerBase
{
    private readonly IAiSettingsService _settingsService;

    public AiSettingsController(IAiSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Gets the current AI provider configuration with masked keys.
    /// </summary>
    [HttpGet]
    public ActionResult<AiProviderConfigDto> GetConfig()
    {
        var config = _settingsService.GetCurrentConfig();
        return Ok(config);
    }

    /// <summary>
    /// Updates the active AI provider and its configuration.
    /// Keys are stored server-side only - never in the database or client.
    /// </summary>
    [HttpPut]
    public IActionResult UpdateConfig([FromBody] UpdateAiProviderRequest request)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        _settingsService.UpdateConfig(request);
        return Ok(_settingsService.GetCurrentConfig());
    }

    /// <summary>
    /// Tests the connection to the specified AI provider without persisting the configuration.
    /// Validates that the provided credentials work without exposing them back to the frontend.
    /// </summary>
    [HttpPost("test-connection")]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(
        [FromBody] UpdateAiProviderRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return BadRequest(new TestConnectionResult
            {
                Success = false,
                Message = validationError,
            });

        var result = await _settingsService.TestConnectionAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns the list of supported AI provider types.
    /// </summary>
    [HttpGet("providers")]
    public ActionResult<IEnumerable<object>> GetProviders()
    {
        var providers = new[]
        {
            new { id = AiProviderType.Fake, name = "Fake (Development)", description = "Deterministic fake provider for testing" },
            new { id = AiProviderType.OpenAi, name = "OpenAI", description = "GPT-4o, GPT-4o-mini, and other OpenAI models" },
            new { id = AiProviderType.AzureOpenAi, name = "Azure OpenAI", description = "OpenAI models hosted on Azure" },
            new { id = AiProviderType.AwsBedrock, name = "AWS Bedrock", description = "Claude, Titan, and other models via AWS" },
            new { id = AiProviderType.Ollama, name = "Ollama", description = "Local models via Ollama (fully offline)" },
            new { id = AiProviderType.Grok, name = "Grok (xAI)", description = "Grok models via xAI API" },
        };

        return Ok(providers);
    }

    /// <summary>
    /// Lists available models for the currently configured provider.
    /// For OpenAI/Grok, fetches from the API using the stored key.
    /// Returns a curated list of chat-capable models.
    /// </summary>
    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken ct)
    {
        var config = _settingsService.GetCurrentConfig();

        switch (config.ActiveProvider)
        {
            case AiProviderType.OpenAi:
            {
                var apiKey = _settingsService.GetRawSetting("Ai:OpenAi:ApiKey");
                if (string.IsNullOrWhiteSpace(apiKey))
                    return Ok(new { models = GetDefaultOpenAiModels() });

                try
                {
                    var models = await FetchOpenAiModelsAsync(apiKey, ct);
                    return Ok(new { models });
                }
                catch
                {
                    return Ok(new { models = GetDefaultOpenAiModels() });
                }
            }

            case AiProviderType.Ollama:
            {
                var baseUrl = _settingsService.GetRawSetting("Ai:Ollama:BaseUrl") ?? "http://localhost:11434";
                try
                {
                    var models = await FetchOllamaModelsAsync(baseUrl, ct);
                    return Ok(new { models });
                }
                catch
                {
                    return Ok(new { models = Array.Empty<string>() });
                }
            }

            case AiProviderType.Grok:
            {
                return Ok(new { models = new[] { "grok-3-mini", "grok-3", "grok-2" } });
            }

            default:
                return Ok(new { models = Array.Empty<string>() });
        }
    }

    private static string[] GetDefaultOpenAiModels() =>
    [
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4-turbo",
        "gpt-4",
        "gpt-3.5-turbo",
    ];

    private static async Task<string[]> FetchOpenAiModelsAsync(string apiKey, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.GetAsync("https://api.openai.com/v1/models", ct);
        if (!response.IsSuccessStatusCode)
            return GetDefaultOpenAiModels();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        // Exclude non-chat models (audio, image, realtime, transcribe, tts, instruct, search, embed)
        var excludePatterns = new[] { "audio", "image", "realtime", "transcribe", "tts", "instruct", "search", "embed", "whisper", "translate", "codex", "nano", "deep-research", "diarize" };

        var chatModels = new List<string>();
        foreach (var model in data.EnumerateArray())
        {
            var id = model.GetProperty("id").GetString();
            if (id is null) continue;
            if (!(id.StartsWith("gpt-") || id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("o4")))
                continue;
            if (excludePatterns.Any(p => id.Contains(p, StringComparison.OrdinalIgnoreCase)))
                continue;
            chatModels.Add(id);
        }

        chatModels.Sort();
        return chatModels.Count > 0 ? chatModels.ToArray() : GetDefaultOpenAiModels();
    }

    private static async Task<string[]> FetchOllamaModelsAsync(string baseUrl, CancellationToken ct)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/api/tags", ct);
        if (!response.IsSuccessStatusCode)
            return [];

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("models", out var models))
            return [];

        var modelNames = new List<string>();
        foreach (var model in models.EnumerateArray())
        {
            var name = model.GetProperty("name").GetString();
            if (name is not null)
                modelNames.Add(name);
        }

        return modelNames.ToArray();
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private static string? ValidateRequest(UpdateAiProviderRequest request)
    {
        return request.ActiveProvider switch
        {
            AiProviderType.OpenAi when request.OpenAi is null =>
                "OpenAI settings are required when selecting the OpenAI provider.",
            AiProviderType.AzureOpenAi when request.AzureOpenAi is null =>
                "Azure OpenAI settings are required when selecting the Azure OpenAI provider.",
            AiProviderType.AwsBedrock when request.AwsBedrock is null =>
                "AWS Bedrock settings are required when selecting the AWS Bedrock provider.",
            AiProviderType.Ollama when request.Ollama is null =>
                "Ollama settings are required when selecting the Ollama provider.",
            AiProviderType.Grok when request.Grok is null =>
                "Grok settings are required when selecting the Grok provider.",
            _ => null,
        };
    }
}
