using Aircane.Application.Retrieval;

namespace Aircane.Infrastructure.Retrieval.Reranking;

/// <summary>
/// The default "no reranking" provider. Always available and needs no configuration; it returns the
/// candidates in their original order with strictly descending scores (1.0, 0.99, 0.98, ...).
/// </summary>
public sealed class NullRerankingProvider : IRerankingProvider
{
    public const string Id = "None";

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        CancellationToken ct = default) =>
        Task.FromResult(PreserveOrder(candidates));

    /// <inheritdoc />
    public Task<RerankingConnectionResult> CheckConnectionAsync(CancellationToken ct = default) =>
        Task.FromResult(new RerankingConnectionResult(true, "No reranking configured."));

    /// <summary>
    /// Returns every candidate in its input order with descending scores. Also the fallback used by
    /// the other providers when they cannot rerank.
    /// </summary>
    internal static IReadOnlyList<RerankResult> PreserveOrder(IReadOnlyList<RerankCandidate> candidates) =>
        candidates.Select((c, i) => new RerankResult(c.Id, 1.0f - i * 0.01f)).ToList();
}
