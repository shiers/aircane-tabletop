using Aircane.Application.DTOs.AiSettings;

namespace Aircane.Application.Retrieval;

/// <summary>
/// Creates <see cref="IRerankingProvider"/> instances from the live AI settings, so the host can
/// switch reranking providers at runtime without restarting (the same approach as <c>IAiProvider</c>).
/// </summary>
public interface IRerankingProviderFactory
{
    /// <summary>Creates the provider selected by the saved settings (<c>Reranking:Provider</c>, default "None").</summary>
    IRerankingProvider CreateActive();

    /// <summary>
    /// Creates a provider from unsaved settings, e.g. for "Test Connection". Missing or masked
    /// secrets fall back to the saved values.
    /// </summary>
    IRerankingProvider Create(RerankingSettingsDto settings);
}
