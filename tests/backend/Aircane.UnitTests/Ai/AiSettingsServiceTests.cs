using Aircane.Application.DTOs.AiSettings;
using Aircane.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// Unit tests for <see cref="AiSettingsService"/>.
/// </summary>
public class AiSettingsServiceTests
{
    private static AiSettingsService CreateService(
        Dictionary<string, string?>? configValues = null)
    {
        // Use a unique temp path so tests don't read/write the real ai-settings.json
        var tempDir = Path.Combine(Path.GetTempPath(), $"aircane-test-{Guid.NewGuid():N}");
        var defaults = new Dictionary<string, string?>
        {
            ["Storage:DocumentsPath"] = Path.Combine(tempDir, "documents"),
        };

        if (configValues is not null)
        {
            foreach (var (key, value) in configValues)
                defaults[key] = value;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(defaults)
            .Build();

        var services = new ServiceCollection();
        services.AddHttpClient();
        var sp = services.BuildServiceProvider();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        var logger = NullLogger<AiSettingsService>.Instance;

        return new AiSettingsService(config, httpClientFactory, logger);
    }

    // ── MaskKey ───────────────────────────────────────────────────────────────

    [Fact]
    public void MaskKey_NullInput_ReturnsNull()
    {
        Assert.Null(AiSettingsService.MaskKey(null));
    }

    [Fact]
    public void MaskKey_EmptyInput_ReturnsNull()
    {
        Assert.Null(AiSettingsService.MaskKey(""));
    }

    [Fact]
    public void MaskKey_WhitespaceInput_ReturnsNull()
    {
        Assert.Null(AiSettingsService.MaskKey("   "));
    }

    [Fact]
    public void MaskKey_ShortKey_ReturnsMasked()
    {
        Assert.Equal("****", AiSettingsService.MaskKey("abc"));
    }

    [Fact]
    public void MaskKey_ExactlyFourChars_ReturnsMasked()
    {
        Assert.Equal("****", AiSettingsService.MaskKey("abcd"));
    }

    [Fact]
    public void MaskKey_LongerKey_ShowsLastFourChars()
    {
        var result = AiSettingsService.MaskKey("sk-1234567890abcdef");
        Assert.NotNull(result);
        Assert.EndsWith("cdef", result);
        Assert.StartsWith("**", result);
        Assert.DoesNotContain("sk-1234", result);
    }

    [Fact]
    public void MaskKey_PreservesLength()
    {
        var key = "sk-1234567890abcdef";
        var result = AiSettingsService.MaskKey(key);
        Assert.Equal(key.Length, result!.Length);
    }

    // ── GetCurrentConfig ──────────────────────────────────────────────────────

    [Fact]
    public void GetCurrentConfig_DefaultsToFakeProvider()
    {
        var service = CreateService();
        var config = service.GetCurrentConfig();
        Assert.Equal(AiProviderType.Fake, config.ActiveProvider);
    }

    [Fact]
    public void GetCurrentConfig_ReadsProviderFromConfig()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "OpenAI",
        });

        var config = service.GetCurrentConfig();
        Assert.Equal(AiProviderType.OpenAi, config.ActiveProvider);
    }

    [Fact]
    public void GetCurrentConfig_MasksApiKeys()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "OpenAI",
            ["Ai:OpenAi:ApiKey"] = "sk-test1234567890abcdef",
            ["Ai:OpenAi:Model"] = "gpt-4o",
        });

        var config = service.GetCurrentConfig();

        Assert.NotNull(config.OpenAi);
        Assert.NotNull(config.OpenAi.ApiKey);
        Assert.DoesNotContain("sk-test", config.OpenAi.ApiKey);
        Assert.EndsWith("cdef", config.OpenAi.ApiKey);
        Assert.Equal("gpt-4o", config.OpenAi.Model);
    }

    [Fact]
    public void GetCurrentConfig_ReturnsOllamaSettings()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "Ollama",
            ["Ai:Ollama:BaseUrl"] = "http://myhost:11434",
            ["Ai:Ollama:Model"] = "mistral",
        });

        var config = service.GetCurrentConfig();

        Assert.Equal(AiProviderType.Ollama, config.ActiveProvider);
        Assert.NotNull(config.Ollama);
        Assert.Equal("http://myhost:11434", config.Ollama.BaseUrl);
        Assert.Equal("mistral", config.Ollama.Model);
    }

    // ── UpdateConfig ──────────────────────────────────────────────────────────

    [Fact]
    public void UpdateConfig_ChangesActiveProvider()
    {
        var service = CreateService();

        service.UpdateConfig(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.Ollama,
            Ollama = new OllamaSettingsDto
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama3",
            },
        });

        var config = service.GetCurrentConfig();
        Assert.Equal(AiProviderType.Ollama, config.ActiveProvider);
    }

    [Fact]
    public void UpdateConfig_StoresOpenAiSettings()
    {
        var service = CreateService();

        service.UpdateConfig(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.OpenAi,
            OpenAi = new OpenAiSettingsDto
            {
                ApiKey = "sk-newkey1234567890",
                Model = "gpt-4o",
            },
        });

        var config = service.GetCurrentConfig();
        Assert.NotNull(config.OpenAi);
        Assert.Equal("gpt-4o", config.OpenAi.Model);
        // Key should be masked
        Assert.EndsWith("7890", config.OpenAi.ApiKey!);
    }

    [Fact]
    public void UpdateConfig_DoesNotOverwriteKeyWithEmpty()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "OpenAI",
            ["Ai:OpenAi:ApiKey"] = "sk-original1234567890",
        });

        // Update with empty key (simulating masked key not being sent back)
        service.UpdateConfig(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.OpenAi,
            OpenAi = new OpenAiSettingsDto
            {
                ApiKey = null,
                Model = "gpt-4o-mini",
            },
        });

        var config = service.GetCurrentConfig();
        // Original key should still be there (masked)
        Assert.NotNull(config.OpenAi?.ApiKey);
        Assert.EndsWith("7890", config.OpenAi.ApiKey!);
    }

    [Fact]
    public void UpdateConfig_StoresAzureSettings()
    {
        var service = CreateService();

        service.UpdateConfig(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.AzureOpenAi,
            AzureOpenAi = new AzureOpenAiSettingsDto
            {
                ApiKey = "azure-key-1234567890",
                Endpoint = "https://myresource.openai.azure.com/",
                DeploymentName = "gpt-4o-deploy",
                ApiVersion = "2024-06-01",
            },
        });

        var config = service.GetCurrentConfig();
        Assert.Equal(AiProviderType.AzureOpenAi, config.ActiveProvider);
        Assert.NotNull(config.AzureOpenAi);
        Assert.Equal("https://myresource.openai.azure.com/", config.AzureOpenAi.Endpoint);
        Assert.Equal("gpt-4o-deploy", config.AzureOpenAi.DeploymentName);
        Assert.EndsWith("7890", config.AzureOpenAi.ApiKey!);
    }

    // ── TestConnectionAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task TestConnectionAsync_FakeProvider_ReturnsSuccess()
    {
        var service = CreateService();

        var result = await service.TestConnectionAsync(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.Fake,
        });

        Assert.True(result.Success);
        Assert.Equal("Fake", result.ProviderName);
    }

    [Fact]
    public async Task TestConnectionAsync_OpenAi_WithoutKey_ReturnsFail()
    {
        var service = CreateService();

        var result = await service.TestConnectionAsync(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.OpenAi,
            OpenAi = new OpenAiSettingsDto { ApiKey = null, Model = "gpt-4o" },
        });

        Assert.False(result.Success);
        Assert.Contains("API key is required", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_AzureOpenAi_WithoutEndpoint_ReturnsFail()
    {
        var service = CreateService();

        var result = await service.TestConnectionAsync(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.AzureOpenAi,
            AzureOpenAi = new AzureOpenAiSettingsDto
            {
                ApiKey = "some-key",
                Endpoint = null,
            },
        });

        Assert.False(result.Success);
        Assert.Contains("endpoint is required", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_AwsBedrock_WithoutAccessKey_ReturnsFail()
    {
        var service = CreateService();

        var result = await service.TestConnectionAsync(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.AwsBedrock,
            AwsBedrock = new AwsBedrockSettingsDto
            {
                AccessKeyId = null,
                SecretAccessKey = "secret",
                Region = "us-east-1",
            },
        });

        Assert.False(result.Success);
        Assert.Contains("Access Key ID is required", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_AwsBedrock_WithAllFields_ReturnsSuccess()
    {
        var service = CreateService();

        var result = await service.TestConnectionAsync(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.AwsBedrock,
            AwsBedrock = new AwsBedrockSettingsDto
            {
                AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
                SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                Region = "us-east-1",
                ModelId = "anthropic.claude-3-sonnet",
            },
        });

        Assert.True(result.Success);
        Assert.Equal("AWS Bedrock", result.ProviderName);
    }
}
