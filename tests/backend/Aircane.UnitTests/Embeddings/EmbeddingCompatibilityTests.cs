using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Embeddings;

/// <summary>
/// Unit tests for <see cref="EmbeddingCompatibility"/>: enables vector search when the provider
/// dimension matches the column, disables it otherwise.
/// </summary>
public class EmbeddingCompatibilityTests
{
    private sealed class StubProvider : IEmbeddingProvider
    {
        public int Dimensions { get; init; }
        public string ProviderName => "Stub";
        public string ModelName => "stub-model";
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
            => Task.FromResult(new float[Dimensions]);
        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[Dimensions]).ToList());
    }

    private static IConfiguration Config(int? columnDimension = null)
    {
        var dict = new Dictionary<string, string?>();
        if (columnDimension is int d)
            dict["Embeddings:ColumnDimension"] = d.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void MatchingDimension_EnablesVectorSearch()
    {
        var sut = new EmbeddingCompatibility(
            new StubProvider { Dimensions = 768 },
            Config(),
            NullLogger<EmbeddingCompatibility>.Instance);

        Assert.True(sut.IsVectorSearchEnabled);
        Assert.Equal(768, sut.ColumnDimension);
        Assert.Equal(768, sut.ProviderDimension);
    }

    [Fact]
    public void MismatchedDimension_DisablesVectorSearch()
    {
        var sut = new EmbeddingCompatibility(
            new StubProvider { Dimensions = 1536 },
            Config(columnDimension: 768),
            NullLogger<EmbeddingCompatibility>.Instance);

        Assert.False(sut.IsVectorSearchEnabled);
        Assert.Equal(768, sut.ColumnDimension);
        Assert.Equal(1536, sut.ProviderDimension);
    }

    [Fact]
    public void ConfiguredColumnDimension_MatchesProvider_Enables()
    {
        var sut = new EmbeddingCompatibility(
            new StubProvider { Dimensions = 1536 },
            Config(columnDimension: 1536),
            NullLogger<EmbeddingCompatibility>.Instance);

        Assert.True(sut.IsVectorSearchEnabled);
    }
}
