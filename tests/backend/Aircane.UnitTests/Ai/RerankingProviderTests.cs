using System.Net;
using System.Text;
using System.Text.Json;
using Aircane.Application.DTOs.AiSettings;
using Aircane.Application.Retrieval;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Retrieval.Reranking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>Tests for the reranking providers, their factory and their settings.</summary>
public class RerankingProviderTests
{
    private static readonly IReadOnlyList<RerankCandidate> Candidates =
    [
        new("a", "Grappling uses an Athletics check."),
        new("b", "Opportunity attacks need reach."),
        new("c", "The Actor feat grants mimicry."),
    ];

    // ── Null provider ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NullRerankingProvider_PreservesInputOrder()
    {
        var provider = new NullRerankingProvider();

        var results = await provider.RerankAsync("anything", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
        Assert.Equal("None", provider.ProviderId);
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public async Task NullRerankingProvider_AssignsDescendingScores()
    {
        var results = await new NullRerankingProvider().RerankAsync("anything", Candidates);

        Assert.Equal(1.0f, results[0].Score);
        Assert.Equal(0.99f, results[1].Score, 4);
        Assert.Equal(0.98f, results[2].Score, 4);
    }

    // ── Cohere ────────────────────────────────────────────────────────────────

    private static CohereRerankingProvider Cohere(StubHandler handler, string? apiKey = "test-key") =>
        new(new HttpClient(handler), apiKey, model: null, NullLogger<CohereRerankingProvider>.Instance);

    [Fact]
    public async Task CohereRerankingProvider_MapsResponseCorrectly()
    {
        // Cohere returns results sorted by relevance, each with the original document index.
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"results":[{"index":2,"relevance_score":0.91},{"index":0,"relevance_score":0.40},{"index":1,"relevance_score":0.02}]}""");

        var results = await Cohere(handler).RerankAsync("explain the actor feat", Candidates);

        Assert.Equal(["c", "a", "b"], results.Select(r => r.Id));
        Assert.Equal(0.91f, results[0].Score, 4);
        Assert.Equal(0.02f, results[2].Score, 4);

        // Request shape: bearer auth, model, query, documents in order, top_n = count.
        Assert.Equal("https://api.cohere.com/v1/rerank", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.LastRequest.Headers.Authorization!.ToString());
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("rerank-english-v3.0", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("explain the actor feat", body.RootElement.GetProperty("query").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("top_n").GetInt32());
        Assert.False(body.RootElement.GetProperty("return_documents").GetBoolean());
        Assert.Equal(
            Candidates.Select(c => c.Text),
            body.RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetString()));
    }

    [Fact]
    public async Task CohereRerankingProvider_ResultsOmittingACandidate_StillCoverEveryCandidate()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"results":[{"index":1,"relevance_score":0.7}]}""");

        var results = await Cohere(handler).RerankAsync("q", Candidates);

        Assert.Equal(3, results.Count);
        Assert.Equal("b", results[0].Id);
        Assert.Equal(["a", "c"], results.Skip(1).Select(r => r.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CohereRerankingProvider_HttpError_FallsBackToOriginalOrder(HttpStatusCode status)
    {
        var results = await Cohere(new StubHandler(status, "{}")).RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
    }

    [Fact]
    public async Task CohereRerankingProvider_Returns401_FallsBackToOriginalOrder()
    {
        var results = await Cohere(new StubHandler(HttpStatusCode.Unauthorized, """{"message":"invalid api token"}"""))
            .RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
    }

    [Fact]
    public async Task CohereRerankingProvider_Returns429_FallsBackToOriginalOrder()
    {
        var results = await Cohere(new StubHandler(HttpStatusCode.TooManyRequests, "{}"))
            .RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
    }

    [Fact]
    public async Task CohereRerankingProvider_NetworkFailure_FallsBackWithoutThrowing()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}") { Throw = new HttpRequestException("boom") };

        var results = await Cohere(handler).RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
    }

    [Fact]
    public async Task CohereRerankingProvider_MalformedBody_FallsBackWithoutThrowing()
    {
        var results = await Cohere(new StubHandler(HttpStatusCode.OK, "not json")).RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CohereRerankingProvider_IsAvailable_FalseWhenNoApiKey(string? apiKey)
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var provider = Cohere(handler, apiKey);

        Assert.False(provider.IsAvailable);
        // And it makes no network call when asked to rerank without a key.
        var results = await provider.RerankAsync("q", Candidates);
        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CohereRerankingProvider_CheckConnection_ReportsInvalidKey()
    {
        var result = await Cohere(new StubHandler(HttpStatusCode.Unauthorized, "{}")).CheckConnectionAsync();

        Assert.False(result.Available);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CohereRerankingProvider_CheckConnection_Succeeds()
    {
        var result = await Cohere(new StubHandler(HttpStatusCode.OK,
            """{"results":[{"index":0,"relevance_score":0.5}]}""")).CheckConnectionAsync();

        Assert.True(result.Available);
    }

    // ── Ollama ────────────────────────────────────────────────────────────────

    private static OllamaRerankingProvider Ollama(StubHandler handler, string baseUrl = "http://ollama.test:11434")
    {
        OllamaRerankingProvider.ClearAvailabilityCache();
        return new OllamaRerankingProvider(new HttpClient(handler), baseUrl, model: null,
            NullLogger<OllamaRerankingProvider>.Instance);
    }

    [Fact]
    public void OllamaRerankingProvider_BuildsCrossEncoderInput_Correctly()
    {
        Assert.Equal(
            "[CLS] how does grappling work [SEP] A grappled creature has speed 0. [SEP]",
            OllamaRerankingProvider.BuildInput("how does grappling work", "A grappled creature has speed 0."));
    }

    [Fact]
    public async Task OllamaRerankingProvider_SendsInputToEmbedEndpoint_AndRanksByFirstValue()
    {
        // The first embedding value is the score: b (0.9) > c (0.5) > a (0.1).
        var scores = new Queue<string>(["0.1", "0.9", "0.5"]);
        var handler = new StubHandler(HttpStatusCode.OK, "")
        {
            Respond = _ => $$"""{"embeddings":[[{{scores.Dequeue()}},0.0,0.0]]}""",
        };

        var results = await Ollama(handler).RerankAsync("my question", Candidates);

        Assert.Equal(["b", "c", "a"], results.Select(r => r.Id));
        Assert.Equal("http://ollama.test:11434/api/embed", handler.LastRequest!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("bge-reranker-base", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "[CLS] my question [SEP] The Actor feat grants mimicry. [SEP]",
            body.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task OllamaRerankingProvider_ModelNotFound_FallsBackToOriginalOrder()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"error":"model \"bge-reranker-base\" not found"}""");

        var results = await Ollama(handler).RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
        Assert.Equal(1, handler.RequestCount); // stops after the first 404 instead of retrying per chunk
    }

    [Fact]
    public async Task OllamaRerankingProvider_ConnectionFailure_FallsBackWithoutThrowing()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}") { Throw = new HttpRequestException("refused") };

        var results = await Ollama(handler).RerankAsync("q", Candidates);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Id));
    }

    [Fact]
    public void OllamaRerankingProvider_IsAvailable_FalseWhenModelMissing_AndCached()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "{}");
        var provider = Ollama(handler);

        Assert.False(provider.IsAvailable);
        Assert.False(provider.IsAvailable);

        Assert.Equal(1, handler.RequestCount); // second read served from the 60 s cache
    }

    [Fact]
    public void OllamaRerankingProvider_IsAvailable_TrueWhenModelAnswers()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"embeddings":[[0.3,0.1]]}""");

        Assert.True(Ollama(handler).IsAvailable);
    }

    // ── Factory and settings ──────────────────────────────────────────────────

    private static (AiSettingsService Settings, RerankingProviderFactory Factory) CreateSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"aircane-test-{Guid.NewGuid():N}");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DocumentsPath"] = Path.Combine(tempDir, "documents") })
            .Build();
        var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var settings = new AiSettingsService(
            config, services.GetRequiredService<IHttpClientFactory>(), NullLogger<AiSettingsService>.Instance);
        var factory = new RerankingProviderFactory(
            settings, services.GetRequiredService<IHttpClientFactory>(), NullLoggerFactory.Instance);
        return (settings, factory);
    }

    [Fact]
    public void Settings_DefaultToNoReranking()
    {
        var (settings, factory) = CreateSettings();

        var reranking = settings.GetCurrentConfig().Reranking!;

        Assert.Equal("None", reranking.Provider);
        Assert.Equal("bge-reranker-base", reranking.Ollama!.Model);
        Assert.Equal("rerank-english-v3.0", reranking.Cohere!.Model);
        Assert.Null(reranking.Cohere.ApiKey);
        Assert.Equal("None", factory.CreateActive().ProviderId);
    }

    [Fact]
    public void Settings_SaveReranking_MasksCohereKey_AndSwitchesActiveProvider()
    {
        var (settings, factory) = CreateSettings();

        settings.UpdateConfig(new UpdateAiProviderRequest
        {
            ActiveProvider = AiProviderType.Fake,
            Reranking = new RerankingSettingsDto
            {
                Provider = "Cohere",
                Cohere = new CohereRerankingSettingsDto { ApiKey = "sk-secret-1234", Model = "rerank-english-v3.0" },
            },
        });

        var reranking = settings.GetCurrentConfig().Reranking!;
        Assert.Equal("Cohere", reranking.Provider);
        Assert.Equal("**********1234", reranking.Cohere!.ApiKey); // last 4 only, never the full key
        Assert.Equal("sk-secret-1234", settings.GetRawSetting("Reranking:Cohere:ApiKey"));
        Assert.Equal("Cohere", factory.CreateActive().ProviderId);
        Assert.True(factory.CreateActive().IsAvailable);
    }

    [Fact]
    public void Settings_SavingAMaskedKey_KeepsTheSavedKey()
    {
        var (settings, _) = CreateSettings();
        settings.UpdateConfig(new UpdateAiProviderRequest
        {
            Reranking = new RerankingSettingsDto
            {
                Provider = "Cohere",
                Cohere = new CohereRerankingSettingsDto { ApiKey = "sk-secret-1234" },
            },
        });

        // The UI echoes the masked value back when the user did not retype the key.
        settings.UpdateConfig(new UpdateAiProviderRequest
        {
            Reranking = new RerankingSettingsDto
            {
                Provider = "Cohere",
                Cohere = new CohereRerankingSettingsDto { ApiKey = "**********1234" },
            },
        });

        Assert.Equal("sk-secret-1234", settings.GetRawSetting("Reranking:Cohere:ApiKey"));
    }

    [Fact]
    public void Settings_RequestWithoutReranking_LeavesRerankingUnchanged()
    {
        var (settings, _) = CreateSettings();
        settings.UpdateConfig(new UpdateAiProviderRequest
        {
            Reranking = new RerankingSettingsDto { Provider = "Ollama" },
        });

        settings.UpdateConfig(new UpdateAiProviderRequest { ActiveProvider = AiProviderType.Fake });

        Assert.Equal("Ollama", settings.GetCurrentConfig().Reranking!.Provider);
    }

    [Fact]
    public void Settings_UnknownProvider_IsStoredAsNone()
    {
        var (settings, factory) = CreateSettings();

        settings.UpdateConfig(new UpdateAiProviderRequest
        {
            Reranking = new RerankingSettingsDto { Provider = "Mystery" },
        });

        Assert.Equal("None", settings.GetCurrentConfig().Reranking!.Provider);
        Assert.Equal("None", factory.CreateActive().ProviderId);
    }

    [Fact]
    public void Factory_CreateFromUnsavedSettings_FallsBackToSavedKeyWhenMasked()
    {
        var (settings, factory) = CreateSettings();
        settings.UpdateConfig(new UpdateAiProviderRequest
        {
            Reranking = new RerankingSettingsDto
            {
                Provider = "None",
                Cohere = new CohereRerankingSettingsDto { ApiKey = "sk-secret-1234" },
            },
        });

        var withMaskedKey = factory.Create(new RerankingSettingsDto
        {
            Provider = "Cohere",
            Cohere = new CohereRerankingSettingsDto { ApiKey = "**********1234" },
        });
        var withNoKey = factory.Create(new RerankingSettingsDto { Provider = "Cohere" });

        Assert.True(withMaskedKey.IsAvailable);
        Assert.True(withNoKey.IsAvailable);
    }

    [Fact]
    public void Factory_CreateCohereWithoutAnyKey_IsUnavailable()
    {
        var (_, factory) = CreateSettings();

        Assert.False(factory.Create(new RerankingSettingsDto { Provider = "Cohere" }).IsAvailable);
    }

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Exception? Throw { get; init; }
        public Func<HttpRequestMessage, string>? Respond { get; init; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            if (Throw is not null)
                throw Throw;

            var content = Respond?.Invoke(request) ?? body;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }
    }
}
