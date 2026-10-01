namespace Aircane.Application.DTOs.Setup;

/// <summary>
/// First-launch / configuration status for the setup wizard.
/// Returned by <c>GET /api/setup/status</c> (anonymous — called before any session exists).
/// Exposes only booleans and provider display names; never file paths, keys, or secrets.
/// </summary>
/// <param name="IsFirstLaunch">
/// True when the local <c>ai-settings.json</c> file does not exist (a brand-new install
/// that has never persisted AI settings).
/// </param>
/// <param name="AiProviderConfigured">
/// True when the active chat AI provider is something other than the shipped
/// <c>Fake</c> default (i.e. the host has chosen a real provider).
/// </param>
/// <param name="ActiveAiProvider">
/// Display name of the active chat AI provider, e.g. <c>Fake</c>, <c>Ollama</c>, <c>OpenAI</c>.
/// </param>
/// <param name="ActiveEmbeddingProvider">
/// Display name of the active embedding provider used for RAG retrieval, e.g.
/// <c>Fake</c> or <c>Ollama</c>.
/// </param>
/// <param name="OllamaReachable">
/// Result of a short-timeout probe of the configured Ollama daemon. False when Ollama
/// is not running or not installed.
/// </param>
public sealed record SetupStatusDto(
    bool IsFirstLaunch,
    bool AiProviderConfigured,
    string ActiveAiProvider,
    string ActiveEmbeddingProvider,
    bool OllamaReachable);
