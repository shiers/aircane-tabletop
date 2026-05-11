using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.AiSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Manages AI provider configuration at runtime.
/// <para>
/// Keys are stored in-memory for the current process lifetime and read from
/// environment variables / user secrets on startup. They are never persisted
/// to the database or exposed to the frontend unmasked.
/// </para>
/// </summary>
public sealed class AiSettingsService : IAiSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiSettingsService> _logger;
    private readonly object _lock = new();

    // In-memory runtime overrides (survive until process restart)
    private AiProviderType _activeProvider;
    private Dictionary<string, string> _runtimeSettings = new();

    public AiSettingsService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AiSettingsService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Initialize from configuration
        _activeProvider = ParseProviderFromConfig(configuration["Ai:Provider"] ?? "Fake");
        LoadSettingsFromConfig();
    }

    /// <inheritdoc />
    public AiProviderConfigDto GetCurrentConfig()
    {
        lock (_lock)
        {
            return new AiProviderConfigDto
            {
                ActiveProvider = _activeProvider,
                OpenAi = new OpenAiSettingsDto
                {
                    ApiKey = MaskKey(GetSetting("Ai:OpenAi:ApiKey")),
                    Model = GetSetting("Ai:OpenAi:Model") ?? "gpt-4o-mini",
                },
                AzureOpenAi = new AzureOpenAiSettingsDto
                {
                    ApiKey = MaskKey(GetSetting("Ai:AzureOpenAi:ApiKey")),
                    Endpoint = GetSetting("Ai:AzureOpenAi:Endpoint"),
                    DeploymentName = GetSetting("Ai:AzureOpenAi:DeploymentName"),
                    ApiVersion = GetSetting("Ai:AzureOpenAi:ApiVersion") ?? "2024-02-01",
                },
                AwsBedrock = new AwsBedrockSettingsDto
                {
                    AccessKeyId = MaskKey(GetSetting("Ai:AwsBedrock:AccessKeyId")),
                    SecretAccessKey = MaskKey(GetSetting("Ai:AwsBedrock:SecretAccessKey")),
                    Region = GetSetting("Ai:AwsBedrock:Region") ?? "us-east-1",
                    ModelId = GetSetting("Ai:AwsBedrock:ModelId"),
                },
                Ollama = new OllamaSettingsDto
                {
                    BaseUrl = GetSetting("Ai:Ollama:BaseUrl") ?? "http://localhost:11434",
                    Model = GetSetting("Ai:Ollama:Model") ?? "llama3",
                },
                Grok = new GrokSettingsDto
                {
                    ApiKey = MaskKey(GetSetting("Ai:Grok:ApiKey")),
                    Model = GetSetting("Ai:Grok:Model") ?? "grok-3-mini",
                },
            };
        }
    }

    /// <inheritdoc />
    public void UpdateConfig(UpdateAiProviderRequest request)
    {
        lock (_lock)
        {
            _activeProvider = request.ActiveProvider;

            // Store provider-specific settings in runtime overrides
            switch (request.ActiveProvider)
            {
                case AiProviderType.OpenAi:
                    if (request.OpenAi is not null)
                    {
                        SetSettingIfNotEmpty("Ai:OpenAi:ApiKey", request.OpenAi.ApiKey);
                        SetSetting("Ai:OpenAi:Model", request.OpenAi.Model);
                    }
                    break;

                case AiProviderType.AzureOpenAi:
                    if (request.AzureOpenAi is not null)
                    {
                        SetSettingIfNotEmpty("Ai:AzureOpenAi:ApiKey", request.AzureOpenAi.ApiKey);
                        SetSettingIfNotEmpty("Ai:AzureOpenAi:Endpoint", request.AzureOpenAi.Endpoint);
                        SetSettingIfNotEmpty("Ai:AzureOpenAi:DeploymentName", request.AzureOpenAi.DeploymentName);
                        SetSettingIfNotEmpty("Ai:AzureOpenAi:ApiVersion", request.AzureOpenAi.ApiVersion);
                    }
                    break;

                case AiProviderType.AwsBedrock:
                    if (request.AwsBedrock is not null)
                    {
                        SetSettingIfNotEmpty("Ai:AwsBedrock:AccessKeyId", request.AwsBedrock.AccessKeyId);
                        SetSettingIfNotEmpty("Ai:AwsBedrock:SecretAccessKey", request.AwsBedrock.SecretAccessKey);
                        SetSettingIfNotEmpty("Ai:AwsBedrock:Region", request.AwsBedrock.Region);
                        SetSettingIfNotEmpty("Ai:AwsBedrock:ModelId", request.AwsBedrock.ModelId);
                    }
                    break;

                case AiProviderType.Ollama:
                    if (request.Ollama is not null)
                    {
                        SetSetting("Ai:Ollama:BaseUrl", request.Ollama.BaseUrl);
                        SetSetting("Ai:Ollama:Model", request.Ollama.Model);
                    }
                    break;

                case AiProviderType.Grok:
                    if (request.Grok is not null)
                    {
                        SetSettingIfNotEmpty("Ai:Grok:ApiKey", request.Grok.ApiKey);
                        SetSetting("Ai:Grok:Model", request.Grok.Model);
                    }
                    break;

                case AiProviderType.Fake:
                    // No settings needed
                    break;
            }

            // Update the Ai:Provider key so DI can pick it up on next resolution
            SetSetting("Ai:Provider", ProviderTypeToConfigName(request.ActiveProvider));

            _logger.LogInformation(
                "AI provider configuration updated. ActiveProvider={Provider}",
                request.ActiveProvider);
        }
    }

    /// <inheritdoc />
    public async Task<TestConnectionResult> TestConnectionAsync(
        UpdateAiProviderRequest request,
        CancellationToken ct = default)
    {
        try
        {
            return request.ActiveProvider switch
            {
                AiProviderType.OpenAi => await TestOpenAiAsync(request.OpenAi, ct),
                AiProviderType.AzureOpenAi => await TestAzureOpenAiAsync(request.AzureOpenAi, ct),
                AiProviderType.Ollama => await TestOllamaAsync(request.Ollama, ct),
                AiProviderType.Grok => await TestGrokAsync(request.Grok, ct),
                AiProviderType.AwsBedrock => await TestAwsBedrockAsync(request.AwsBedrock),
                AiProviderType.Fake => new TestConnectionResult
                {
                    Success = true,
                    Message = "Fake provider is always available.",
                    ProviderName = "Fake",
                },
                _ => new TestConnectionResult
                {
                    Success = false,
                    Message = $"Unknown provider: {request.ActiveProvider}",
                },
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI provider connection test failed for {Provider}", request.ActiveProvider);
            return new TestConnectionResult
            {
                Success = false,
                Message = $"Connection test failed: {ex.Message}",
                ProviderName = request.ActiveProvider.ToString(),
            };
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<TestConnectionResult> TestOpenAiAsync(
        OpenAiSettingsDto? settings, CancellationToken ct)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.ApiKey))
            return Fail("OpenAI API key is required.");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        // Use the models endpoint as a lightweight connectivity check
        var response = await client.GetAsync("https://api.openai.com/v1/models", ct);

        if (response.IsSuccessStatusCode)
        {
            return new TestConnectionResult
            {
                Success = true,
                Message = "Successfully connected to OpenAI API.",
                ProviderName = "OpenAI",
                Model = settings.Model,
            };
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var truncated = body.Length > 200 ? body[..200] : body;
        return Fail($"OpenAI returned HTTP {(int)response.StatusCode}: {truncated}");
    }

    private async Task<TestConnectionResult> TestAzureOpenAiAsync(
        AzureOpenAiSettingsDto? settings, CancellationToken ct)
    {
        if (settings is null)
            return Fail("Azure OpenAI settings are required.");
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return Fail("Azure OpenAI API key is required.");
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
            return Fail("Azure OpenAI endpoint is required.");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("api-key", settings.ApiKey);

        var apiVersion = settings.ApiVersion ?? "2024-02-01";
        var url = $"{settings.Endpoint.TrimEnd('/')}/openai/deployments?api-version={apiVersion}";

        var response = await client.GetAsync(url, ct);

        if (response.IsSuccessStatusCode)
        {
            return new TestConnectionResult
            {
                Success = true,
                Message = "Successfully connected to Azure OpenAI.",
                ProviderName = "Azure OpenAI",
                Model = settings.DeploymentName,
            };
        }

        return Fail($"Azure OpenAI returned HTTP {(int)response.StatusCode}.");
    }

    private async Task<TestConnectionResult> TestOllamaAsync(
        OllamaSettingsDto? settings, CancellationToken ct)
    {
        if (settings is null)
            return Fail("Ollama settings are required.");

        var client = _httpClientFactory.CreateClient();
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        // Ollama exposes a /api/tags endpoint to list available models
        var response = await client.GetAsync($"{baseUrl}/api/tags", ct);

        if (response.IsSuccessStatusCode)
        {
            return new TestConnectionResult
            {
                Success = true,
                Message = "Successfully connected to Ollama.",
                ProviderName = "Ollama",
                Model = settings.Model,
            };
        }

        return Fail($"Ollama returned HTTP {(int)response.StatusCode}. Is Ollama running at {baseUrl}?");
    }

    private async Task<TestConnectionResult> TestGrokAsync(
        GrokSettingsDto? settings, CancellationToken ct)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.ApiKey))
            return Fail("Grok API key is required.");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        // xAI uses an OpenAI-compatible API at api.x.ai
        var response = await client.GetAsync("https://api.x.ai/v1/models", ct);

        if (response.IsSuccessStatusCode)
        {
            return new TestConnectionResult
            {
                Success = true,
                Message = "Successfully connected to Grok (xAI) API.",
                ProviderName = "Grok",
                Model = settings.Model,
            };
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var truncated = body.Length > 200 ? body[..200] : body;
        return Fail($"Grok API returned HTTP {(int)response.StatusCode}: {truncated}");
    }

    private static Task<TestConnectionResult> TestAwsBedrockAsync(AwsBedrockSettingsDto? settings)
    {
        // AWS Bedrock requires the AWS SDK for proper SigV4 signing.
        // For MVP, we validate that the required fields are present.
        if (settings is null)
            return Task.FromResult(Fail("AWS Bedrock settings are required."));
        if (string.IsNullOrWhiteSpace(settings.AccessKeyId))
            return Task.FromResult(Fail("AWS Access Key ID is required."));
        if (string.IsNullOrWhiteSpace(settings.SecretAccessKey))
            return Task.FromResult(Fail("AWS Secret Access Key is required."));
        if (string.IsNullOrWhiteSpace(settings.Region))
            return Task.FromResult(Fail("AWS Region is required."));

        return Task.FromResult(new TestConnectionResult
        {
            Success = true,
            Message = "AWS Bedrock credentials are configured. Full connectivity test requires the AWS SDK (not yet integrated).",
            ProviderName = "AWS Bedrock",
            Model = settings.ModelId,
        });
    }

    private void LoadSettingsFromConfig()
    {
        // Pre-load known settings from IConfiguration (env vars / user secrets)
        var keys = new[]
        {
            "Ai:OpenAi:ApiKey", "Ai:OpenAi:Model",
            "Ai:AzureOpenAi:ApiKey", "Ai:AzureOpenAi:Endpoint",
            "Ai:AzureOpenAi:DeploymentName", "Ai:AzureOpenAi:ApiVersion",
            "Ai:AwsBedrock:AccessKeyId", "Ai:AwsBedrock:SecretAccessKey",
            "Ai:AwsBedrock:Region", "Ai:AwsBedrock:ModelId",
            "Ai:Ollama:BaseUrl", "Ai:Ollama:Model",
            "Ai:Grok:ApiKey", "Ai:Grok:Model",
        };

        foreach (var key in keys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrEmpty(value))
            {
                _runtimeSettings[key] = value;
            }
        }
    }

    private string? GetSetting(string key)
    {
        if (_runtimeSettings.TryGetValue(key, out var value))
            return value;
        return _configuration[key];
    }

    private void SetSetting(string key, string? value)
    {
        if (value is not null)
            _runtimeSettings[key] = value;
    }

    private void SetSettingIfNotEmpty(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _runtimeSettings[key] = value;
    }

    /// <summary>
    /// Masks a key to show only the last 4 characters.
    /// Returns null if the key is null or empty.
    /// </summary>
    public static string? MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (key.Length <= 4)
            return "****";

        return new string('*', key.Length - 4) + key[^4..];
    }

    private static AiProviderType ParseProviderFromConfig(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "openai" => AiProviderType.OpenAi,
            "azureopenai" or "azure" => AiProviderType.AzureOpenAi,
            "awsbedrock" or "bedrock" => AiProviderType.AwsBedrock,
            "ollama" => AiProviderType.Ollama,
            "grok" or "xai" => AiProviderType.Grok,
            _ => AiProviderType.Fake,
        };
    }

    private static string ProviderTypeToConfigName(AiProviderType type)
    {
        return type switch
        {
            AiProviderType.OpenAi => "OpenAI",
            AiProviderType.AzureOpenAi => "AzureOpenAI",
            AiProviderType.AwsBedrock => "AwsBedrock",
            AiProviderType.Ollama => "Ollama",
            AiProviderType.Grok => "Grok",
            _ => "Fake",
        };
    }

    private static TestConnectionResult Fail(string message) =>
        new() { Success = false, Message = message };
}
