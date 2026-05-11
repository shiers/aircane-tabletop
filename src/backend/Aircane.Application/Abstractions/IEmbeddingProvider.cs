namespace Aircane.Application.Abstractions;

/// <summary>
/// Abstraction over an embedding model that converts text into dense float vectors.
/// Implementations include <c>FakeEmbeddingProvider</c> (tests/dev), Ollama, OpenAI, Azure OpenAI, and AWS Bedrock.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>The number of dimensions in each embedding vector (e.g. 768 for nomic-embed-text).</summary>
    int Dimensions { get; }

    /// <summary>Human-readable provider name used for logging and diagnostics (e.g. "Fake", "Ollama").</summary>
    string ProviderName { get; }

    /// <summary>Generates an embedding vector for a single piece of text.</summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Generates embedding vectors for a batch of texts.
    /// Implementations may call the underlying model in parallel or in a single batch request.
    /// </summary>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
