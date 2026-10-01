using Aircane.Infrastructure.Embeddings;
using Xunit;

namespace Aircane.UnitTests.Embeddings;

/// <summary>
/// Unit tests for <see cref="FakeEmbeddingProvider"/>.
/// </summary>
public class FakeEmbeddingProviderTests
{
    private readonly FakeEmbeddingProvider _provider = new();

    // ── Dimensions ────────────────────────────────────────────────────────────

    [Fact]
    public void Dimensions_Returns768()
    {
        Assert.Equal(768, _provider.Dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ReturnsVectorOfCorrectLength()
    {
        var embedding = await _provider.GenerateEmbeddingAsync("hello world");

        Assert.Equal(768, embedding.Length);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ReturnsOneVectorPerInput()
    {
        var texts = new[] { "alpha", "beta", "gamma" };
        var embeddings = await _provider.GenerateEmbeddingsAsync(texts);

        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(768, e.Length));
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateEmbeddingAsync_SameInput_ReturnsSameVector()
    {
        const string text = "The quick brown fox jumps over the lazy dog.";

        var first = await _provider.GenerateEmbeddingAsync(text);
        var second = await _provider.GenerateEmbeddingAsync(text);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SameInputTwice_ReturnsSameVectors()
    {
        var texts = new[] { "fireball", "magic missile", "cure wounds" };

        var first = await _provider.GenerateEmbeddingsAsync(texts);
        var second = await _provider.GenerateEmbeddingsAsync(texts);

        for (int i = 0; i < texts.Length; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    // ── Distinctness ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateEmbeddingAsync_DifferentInputs_ReturnDifferentVectors()
    {
        var a = await _provider.GenerateEmbeddingAsync("attack roll");
        var b = await _provider.GenerateEmbeddingAsync("saving throw");

        // Vectors should not be identical.
        Assert.False(a.SequenceEqual(b),
            "Different inputs should produce different embedding vectors.");
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_DifferentInputs_ReturnDifferentVectors()
    {
        var texts = new[] { "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" };
        var embeddings = await _provider.GenerateEmbeddingsAsync(texts);

        // Every pair of embeddings should be distinct.
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                Assert.False(embeddings[i].SequenceEqual(embeddings[j]),
                    $"Embeddings for '{texts[i]}' and '{texts[j]}' should differ.");
            }
        }
    }

    // ── Unit vector ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateEmbeddingAsync_ReturnsApproximatelyUnitVector()
    {
        var embedding = await _provider.GenerateEmbeddingAsync("normalize me");

        var magnitude = Math.Sqrt(embedding.Sum(v => (double)v * v));
        Assert.InRange(magnitude, 0.999, 1.001);
    }

    // ── Provider metadata ─────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_ReturnsFake()
    {
        Assert.Equal("Fake", _provider.ProviderName);
    }

    // ── Empty input ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateEmbeddingsAsync_EmptyList_ReturnsEmptyList()
    {
        var result = await _provider.GenerateEmbeddingsAsync(Array.Empty<string>());

        Assert.Empty(result);
    }
}
