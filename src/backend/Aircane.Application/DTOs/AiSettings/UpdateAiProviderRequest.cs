namespace Aircane.Application.DTOs.AiSettings;

/// <summary>
/// Request to update the active AI provider configuration.
/// Only the fields relevant to the selected provider need to be populated.
/// </summary>
public sealed class UpdateAiProviderRequest
{
    /// <summary>The provider to activate.</summary>
    public AiProviderType ActiveProvider { get; init; }

    /// <summary>OpenAI settings (required when ActiveProvider is OpenAi).</summary>
    public OpenAiSettingsDto? OpenAi { get; init; }

    /// <summary>Azure OpenAI settings (required when ActiveProvider is AzureOpenAi).</summary>
    public AzureOpenAiSettingsDto? AzureOpenAi { get; init; }

    /// <summary>AWS Bedrock settings (required when ActiveProvider is AwsBedrock).</summary>
    public AwsBedrockSettingsDto? AwsBedrock { get; init; }

    /// <summary>Ollama settings (required when ActiveProvider is Ollama).</summary>
    public OllamaSettingsDto? Ollama { get; init; }

    /// <summary>Grok settings (required when ActiveProvider is Grok).</summary>
    public GrokSettingsDto? Grok { get; init; }
}
