namespace Aircane.Application.Retrieval;

/// <summary>
/// Abstraction over a reranking model that re-scores retrieved chunks against the full question.
/// Implementations include <c>NullRerankingProvider</c> (default, keeps input order), Ollama and Cohere.
/// <para>
/// Reranking is optional: when the active provider is "None" the retrieval pipeline skips the step.
/// <see cref="RerankAsync"/> must never throw because of a provider failure; on any error it returns
/// the candidates in their original order.
/// </para>
/// </summary>
public interface IRerankingProvider
{
    /// <summary>The provider identifier shown in settings and stored in config ("None", "Ollama", "Cohere").</summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether this provider is usable with the current configuration.
    /// The null provider always returns true; others check for an API key or a reachable model.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reranks <paramref name="candidates"/> by relevance to <paramref name="query"/>.
    /// Returns every candidate exactly once, in descending relevance order.
    /// </summary>
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        CancellationToken ct = default);

    /// <summary>
    /// Actively verifies the provider works (a real call, not a cached or configuration-only check)
    /// and explains the outcome. Used by the settings "Test Connection" button.
    /// </summary>
    Task<RerankingConnectionResult> CheckConnectionAsync(CancellationToken ct = default);
}

/// <summary>A chunk to be reranked. <paramref name="Id"/> is echoed back in the <see cref="RerankResult"/>.</summary>
public record RerankCandidate(string Id, string Text);

/// <summary>The relevance score assigned to a candidate; higher is more relevant.</summary>
public record RerankResult(string Id, float Score);

/// <summary>Outcome of <see cref="IRerankingProvider.CheckConnectionAsync"/>.</summary>
public record RerankingConnectionResult(bool Available, string Message);
