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
    /// Updates the active AI provider and its configuration.
    /// Keys are stored in-memory for the current process lifetime.
    /// For persistence across restarts, the host should set environment variables or user secrets.
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
