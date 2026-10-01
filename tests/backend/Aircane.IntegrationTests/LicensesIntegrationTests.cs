using System.Net;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Library;
using Aircane.Workers.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the public license/attribution API (Phase 10.6/10.8).
/// Built-in content is seeded once via the real <see cref="BuiltInContentSeeder"/> against the
/// factory's in-memory database (the Development-only startup seeder does not run under the
/// Testing environment).
/// </summary>
public class LicensesIntegrationTests : IClassFixture<AircaneWebApplicationFactory>, IAsyncLifetime
{
    private readonly AircaneWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LicensesIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        // Idempotent: safe even if a prior test in the shared fixture already seeded.
        var seeder = scope.ServiceProvider.GetRequiredService<BuiltInContentSeeder>();
        await seeder.SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LicensesEndpoint_ReturnsBothBuiltInDocuments()
    {
        var response = await _client.GetAsync("/api/library/licenses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var licenses = await response.Content.ReadFromJsonAsync<List<LicenseInfoDto>>();
        Assert.NotNull(licenses);
        Assert.Equal(2, licenses.Count);
        Assert.All(licenses, l => Assert.True(l.IsBuiltIn));

        Assert.Contains(licenses, l => l.LicenseKey == "cc-by-4.0");
        Assert.Contains(licenses, l => l.LicenseKey == "orc");
        Assert.DoesNotContain(licenses, l => l.LicenseKey == "ogl-1.0a");
    }

    [Fact]
    public async Task LicensesEndpoint_RequiresNoAuthentication()
    {
        // A client with no auth header at all must still reach the endpoint.
        using var anonymousClient = _factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Authorization = null;

        var response = await anonymousClient.GetAsync("/api/library/licenses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
