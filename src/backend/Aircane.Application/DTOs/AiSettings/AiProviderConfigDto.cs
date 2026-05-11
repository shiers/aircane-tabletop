namespace Aircane.Application.DTOs.AiSettings;

/// <summary>
/// DTO representing the current AI provider configuration.
/// Keys are always masked in responses (last 4 chars only).
/// </summary>
public sealed class AiProviderConfigDto
{
    /// <summary>The currently active provider.</summary>
    public AiProviderType ActiveProvider { get; init; }

    /// <summary>OpenAI-specific settings.</summary>
    public OpenAiSettingsDto? OpenAi { get; init; }

    /// <summary>Azure OpenAI-specific settings.</summary>
    public AzureOpenAiSettingsDto? AzureOpenAi { get; init; }

    /// <summary>AWS Bedrock-specific settings.</summary>
    public AwsBedrockSettingsDto? AwsBedrock { get; init; }

    /// <summary>Ollama-specific settings.</summary>
    public OllamaSettingsDto? Ollama { get; init; }

    /// <summary>Grok-specific settings.</summary>
    public GrokSettingsDto? Grok { get; init; }
}

/// <summary>OpenAI provider settings (API key is masked in responses).</summary>
public sealed class OpenAiSettingsDto
{
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "gpt-4o-mini";
}

/// <summary>Azure OpenAI provider settings.</summary>
public sealed class AzureOpenAiSettingsDto
{
    public string? ApiKey { get; init; }
    public string? Endpoint { get; init; }
    public string? DeploymentName { get; init; }
    public string? ApiVersion { get; init; }
}

/// <summary>AWS Bedrock provider settings.</summary>
public sealed class AwsBedrockSettingsDto
{
    public string? AccessKeyId { get; init; }
    public string? SecretAccessKey { get; init; }
    public string? Region { get; init; }
    public string? ModelId { get; init; }
}

/// <summary>Ollama provider settings.</summary>
public sealed class OllamaSettingsDto
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "llama3";
}

/// <summary>Grok (xAI) provider settings (API key is masked in responses).</summary>
public sealed class GrokSettingsDto
{
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "grok-3-mini";
}
