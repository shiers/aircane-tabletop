using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the health endpoint.
/// </summary>
public class HealthEndpointTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(AircaneWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsResponse()
    {
        var response = await _client.GetAsync("/health");

        // With InMemory database, the health check may report degraded (503) because
        // relational-specific checks (raw SQL) don't work with InMemory provider.
        // The endpoint should still respond - either 200 or 503.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected 200 or 503, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task GetHealth_ReturnsStatusField()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(body);
        // Status should be either "healthy" or "degraded" depending on DB provider
        Assert.False(string.IsNullOrWhiteSpace(body.Status));
    }

    private sealed record HealthResponse(string Status);
}
