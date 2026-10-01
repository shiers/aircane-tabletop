namespace Aircane.Application.DTOs.AiSettings;

/// <summary>
/// Supported AI provider types.
/// </summary>
public enum AiProviderType
{
    Fake,
    OpenAi,
    AzureOpenAi,
    AwsBedrock,
    Ollama,
    Grok,
}
