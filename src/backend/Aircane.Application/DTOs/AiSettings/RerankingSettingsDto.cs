namespace Aircane.Application.DTOs.AiSettings;

/// <summary>
/// Reranking configuration. Used both in responses (the Cohere API key is masked to its last 4
/// characters) and in update/test requests (a null or masked key means "keep the saved key").
/// </summary>
public sealed class RerankingSettingsDto
{
    /// <summary>"None", "Ollama" or "Cohere".</summary>
    public string Provider { get; init; } = "None";

    public OllamaRerankingSettingsDto? Ollama { get; init; }

    public CohereRerankingSettingsDto? Cohere { get; init; }
}

/// <summary>
/// Ollama reranking settings: the chat model that scores each chunk. The server URL comes from the
/// Ollama chat provider settings.
/// </summary>
public sealed class OllamaRerankingSettingsDto
{
    public string Model { get; init; } = "llama3.2:3b";
}

/// <summary>Cohere reranking settings (API key is masked in responses).</summary>
public sealed class CohereRerankingSettingsDto
{
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "rerank-english-v3.0";
}
