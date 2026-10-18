using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.AiSettings;
using Aircane.Application.Retrieval;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.Retrieval.Reranking;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests must never call a real AI service, however the developer's settings are set up.
/// </summary>
public class TestAiProviderTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly AircaneWebApplicationFactory _factory;

    public TestAiProviderTests(AircaneWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void IntegrationTests_UsesFakeAiProvider_NotRealProvider()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<FakeAiProvider>(scope.ServiceProvider.GetRequiredService<IAiProvider>());
        Assert.IsType<FakeEmbeddingProvider>(scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>());
        Assert.IsType<NullRerankingProvider>(scope.ServiceProvider.GetRequiredService<IRerankingProvider>());
    }

    [Fact]
    public void IntegrationTests_UseFakeAiProvider_EvenWhenSettingsSelectOpenAi()
    {
        // The same situation as a developer machine with a real OpenAI key in its settings: the settings
        // say OpenAI, and the registered provider must still be the fake.
        var configured = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "OpenAi",
                ["Ai:OpenAi:ApiKey"] = "sk-not-a-real-key",
                ["Embeddings:Provider"] = "OpenAi",
            })));

        using var scope = configured.Services.CreateScope();

        // Prove the scenario is real: the settings do select OpenAI...
        var settings = scope.ServiceProvider.GetRequiredService<IAiSettingsService>().GetCurrentConfig();
        Assert.Equal(AiProviderType.OpenAi, settings.ActiveProvider);

        // ...yet nothing real is used.
        var provider = scope.ServiceProvider.GetRequiredService<IAiProvider>();
        Assert.IsType<FakeAiProvider>(provider);
        Assert.IsNotType<OpenAiProvider>(provider);
        Assert.IsNotType<OllamaProvider>(provider);
        Assert.IsType<FakeEmbeddingProvider>(scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>());
    }
}
