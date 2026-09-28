namespace Aircane.Application.Abstractions;

/// <summary>
/// Reports whether the active embedding provider's vector dimension matches the pgvector column
/// dimension in the database, so callers can gracefully disable vector operations on a mismatch
/// rather than failing at insert/query time.
/// </summary>
/// <remarks>
/// The pgvector <c>embedding</c> column has a fixed dimension (default 768). If the active
/// provider produces a different dimension (e.g. OpenAI's 1536), embeddings cannot be stored or
/// compared until the column is migrated and documents are re-embedded. In that state, keyword
/// search continues to work while vector search is disabled.
/// </remarks>
public interface IEmbeddingCompatibility
{
    /// <summary>The pgvector column dimension the database is configured for.</summary>
    int ColumnDimension { get; }

    /// <summary>The active embedding provider's reported dimension.</summary>
    int ProviderDimension { get; }

    /// <summary>
    /// True when the provider dimension matches the column dimension, so embeddings can be
    /// written and vector search can run. False disables vector operations (keyword search only).
    /// </summary>
    bool IsVectorSearchEnabled { get; }
}
