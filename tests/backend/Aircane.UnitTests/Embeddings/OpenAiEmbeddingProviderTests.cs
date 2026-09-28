using System.Net;
using System.Text;
using System.Text.Json;
using Aircane.Infrastructure.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Embeddings;

/// <summary>
/// Unit tests for <see cref="OpenAiEmbeddingProvider"/> using a stub HTTP handler: verifies the
/// request targets the embeddings endpoint with the configured model and that the response is
/// mapped back in request order.
/// </summary>
public class OpenAiEmbeddingProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public required Func<string> ResponseFactory { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseFactory(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string BuildResponse(params (int Index, float[] Embedding)[] items)
    {
        var data = items.Select(i => new
        {
            index = i.Index,
            embedding = i.Embedding,
        });
        return JsonSerializer.Serialize(new { data });
    }

    [Fact]
    public void Dimensions_And_Model_DefaultCorrectly()
    {
        using var handler = new CapturingHandler { ResponseFactory = () => BuildResponse() };
        using var client = new HttpClient(handler);
        var sut = new OpenAiEmbeddingProvider(client, "sk-test", model: null, dimensions: null,
            NullLogger<OpenAiEmbeddingProvider>.Instance);

        Assert.Equal(OpenAiEmbeddingProvider.DefaultDimensions, sut.Dimensions);
        Assert.Equal(OpenAiEmbeddingProvider.DefaultModel, sut.ModelName);
        Assert.Equal("OpenAI", sut.ProviderName);
    }

    [Fact]
    public async Task GenerateEmbeddings_SendsModel_AndMapsResultsInOrder()
    {
        using var handler = new CapturingHandler
        {
            // Return out-of-order to prove the provider re-orders by index.
            ResponseFactory = () => BuildResponse(
                (1, [0.4f, 0.5f, 0.6f]),
                (0, [0.1f, 0.2f, 0.3f])),
        };
        using var client = new HttpClient(handler);
        var sut = new OpenAiEmbeddingProvider(client, "sk-test", "text-embedding-3-small", 3,
            NullLogger<OpenAiEmbeddingProvider>.Instance);

        var result = await sut.GenerateEmbeddingsAsync(["first", "second"]);

        Assert.Equal(2, result.Count);
        Assert.Equal([0.1f, 0.2f, 0.3f], result[0]); // index 0 first
        Assert.Equal([0.4f, 0.5f, 0.6f], result[1]);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("api.openai.com/v1/embeddings", handler.LastRequestUri!.ToString());
        Assert.Contains("text-embedding-3-small", handler.LastRequestBody);
    }

    [Fact]
    public async Task GenerateEmbedding_Single_ReturnsFirstVector()
    {
        using var handler = new CapturingHandler
        {
            ResponseFactory = () => BuildResponse((0, [1f, 0f])),
        };
        using var client = new HttpClient(handler);
        var sut = new OpenAiEmbeddingProvider(client, "sk-test", "text-embedding-3-small", 2,
            NullLogger<OpenAiEmbeddingProvider>.Instance);

        var vector = await sut.GenerateEmbeddingAsync("hello");
        Assert.Equal([1f, 0f], vector);
    }

    [Fact]
    public void MissingApiKey_Throws()
    {
        using var handler = new CapturingHandler { ResponseFactory = () => BuildResponse() };
        using var client = new HttpClient(handler);
        Assert.Throws<InvalidOperationException>(() =>
            new OpenAiEmbeddingProvider(client, apiKey: "", model: null, dimensions: null,
                NullLogger<OpenAiEmbeddingProvider>.Instance));
    }
}
