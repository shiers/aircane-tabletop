using Aircane.Application.DTOs.AiSettings;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Service for managing AI provider configuration.
/// Keys are stored server-side via environment variables or user secrets - never in the database.
/// </summary>
public interface IAiSettingsService
{
    /// <summary>
    /// Gets the current AI provider configuration with masked keys.
    /// </summary>
    AiProviderConfigDto GetCurrentConfig();

    /// <summary>
    /// Gets a raw (unmasked) setting value by key. Used internally by the DI factory
    /// to pass credentials to provider instances.
    /// </summary>
    string? GetRawSetting(string key);

    /// <summary>
    /// Updates the active AI provider and its configuration.
    /// Settings are persisted to a local file so they survive restarts.
    /// </summary>
    void UpdateConfig(UpdateAiProviderRequest request);

    /// <summary>
    /// Tests the connection to the specified provider using the given credentials.
    /// Does not persist the configuration - only validates connectivity.
    /// </summary>
    Task<TestConnectionResult> TestConnectionAsync(
        UpdateAiProviderRequest request,
        CancellationToken ct = default);
}
