using Aircane.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Embeddings;

/// <summary>
/// Compares the active embedding provider's dimension to the configured pgvector column
/// dimension and logs a clear startup warning when they differ (disabling vector search).
/// Registered as a singleton and evaluated once from the active provider at construction.
/// </summary>
public sealed class EmbeddingCompatibility : IEmbeddingCompatibility
{
    /// <summary>Default pgvector column dimension (matches the initial migration's <c>vector(768)</c>).</summary>
    public const int DefaultColumnDimension = 768;

    public int ColumnDimension { get; }
    public int ProviderDimension { get; }
    public bool IsVectorSearchEnabled { get; }

    public EmbeddingCompatibility(
        IEmbeddingProvider embeddingProvider,
        IConfiguration configuration,
        ILogger<EmbeddingCompatibility> logger)
    {
        ColumnDimension = configuration.GetValue<int?>("Embeddings:ColumnDimension") ?? DefaultColumnDimension;
        ProviderDimension = embeddingProvider.Dimensions;
        IsVectorSearchEnabled = ProviderDimension == ColumnDimension;

        if (!IsVectorSearchEnabled)
        {
            logger.LogWarning(
                "Embedding dimension mismatch: active provider '{Provider}' produces {ProviderDim}-dimensional " +
                "vectors but the database column is vector({ColumnDim}). Vector (semantic) search is DISABLED; " +
                "keyword search still works. To enable vector search, migrate the column " +
                "(ALTER TABLE document_chunks ALTER COLUMN embedding TYPE vector({ProviderDim})), set " +
                "Embeddings:ColumnDimension={ProviderDim}, then re-embed the library " +
                "(POST /api/library/documents/reembed-all). See docs/setup/ai-configuration.md.",
                embeddingProvider.ProviderName, ProviderDimension, ColumnDimension, ProviderDimension, ProviderDimension);
        }
        else
        {
            logger.LogInformation(
                "Embedding provider '{Provider}' dimension ({Dim}) matches the database column. Vector search enabled.",
                embeddingProvider.ProviderName, ProviderDimension);
        }
    }
}
