using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Workers.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for POST /api/characters/import/dndbeyond-url (FEAT-003). A WireMock.Net local
/// server stands in for the unofficial D&amp;D Beyond character-service API; a derived factory points
/// the "DndBeyond" typed client at it. Also asserts the supplied character URL never appears in any
/// captured log output.
/// </summary>
public class DndBeyondUrlImportIntegrationTests : IAsyncLifetime
{
    private const int CharacterId = 1234567;

    private static readonly string DdbCharacterJson =
        """
        {
          "id": 1234567,
          "dateModified": "2024-01-01T00:00:00.000Z",
          "name": "Test Character",
          "race": { "fullName": "Mountain Dwarf", "subRaceShortName": "Mountain" },
          "stats": [
            { "id": 1, "value": 16 }, { "id": 2, "value": 14 }, { "id": 3, "value": 15 },
            { "id": 4, "value": 10 }, { "id": 5, "value": 12 }, { "id": 6, "value": 8 }
          ],
          "hitPointInfo": { "baseHitPoints": 28, "removedHitPoints": 0, "temporaryHitPoints": 0 },
          "armorClass": { "totalArmorClass": 17 },
          "classes": [
            { "level": 4, "definition": { "name": "Fighter", "sources": [ { "sourceId": 1 } ] } }
          ]
        }
        """;

    private WireMockServer _wireMock = null!;
    private DndBeyondWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly CapturingLoggerProvider _logs = new();

    public async Task InitializeAsync()
    {
        _wireMock = WireMockServer.Start();
        _factory = new DndBeyondWebApplicationFactory(_wireMock.Urls[0], _logs);
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<GameSystemDefinitionSeeder>();
        await seeder.SeedAsync();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _wireMock?.Stop();
        _wireMock?.Dispose();
        return Task.CompletedTask;
    }

    private void StubCharacterResponse(int statusCode, string? body)
    {
        var response = Response.Create().WithStatusCode(statusCode);
        if (body is not null)
            response = response.WithHeader("Content-Type", "application/json").WithBody(body);

        _wireMock
            .Given(Request.Create().WithPath($"/character/v5/character/{CharacterId}").UsingGet())
            .RespondWith(response);
    }

    [Fact]
    public async Task Import_Success_ReturnsReviewEnvelope()
    {
        StubCharacterResponse(200, DdbCharacterJson);

        var body = new { characterUrl = $"https://www.dndbeyond.com/characters/{CharacterId}" };
        var response = await _client.PostAsJsonAsync("/api/characters/import/dndbeyond-url", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("DndBeyondApi", root.GetProperty("detectedSource").GetString());
        Assert.True(root.GetProperty("rulesetRequiresConfirmation").GetBoolean());

        var review = root.GetProperty("review");
        Assert.False(string.IsNullOrWhiteSpace(review.GetProperty("gameSystemDefinitionId").GetString()));

        var mapped = review.GetProperty("mappedFields");
        Assert.Equal("Test Character", mapped.GetProperty("identity.name").GetString());
        Assert.Equal("16", mapped.GetProperty("abilities.strength").GetString());
        Assert.Equal("17", mapped.GetProperty("combat.armorClass").GetString());
    }

    [Theory]
    [InlineData(403, "This character is private. Make your character sheet public on D&D Beyond to import it.")]
    [InlineData(404, "Character not found. Check the URL and try again.")]
    [InlineData(500, "D&D Beyond import failed. Use the PDF export option instead.")]
    public async Task Import_UpstreamError_Returns422WithExactMessage(int statusCode, string expectedDetail)
    {
        StubCharacterResponse(statusCode, null);

        var body = new { characterUrl = $"https://www.dndbeyond.com/characters/{CharacterId}" };
        var response = await _client.PostAsJsonAsync("/api/characters/import/dndbeyond-url", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedDetail, json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_Timeout_Returns422NotResponding()
    {
        // Delay the stub well beyond the client's 10s timeout (overridden to a short value in the
        // test factory) so the request times out.
        _wireMock
            .Given(Request.Create().WithPath($"/character/v5/character/{CharacterId}").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(DdbCharacterJson)
                .WithDelay(TimeSpan.FromSeconds(5)));

        var body = new { characterUrl = $"https://www.dndbeyond.com/characters/{CharacterId}" };
        var response = await _client.PostAsJsonAsync("/api/characters/import/dndbeyond-url", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "D&D Beyond is not responding. Try again later.",
            json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_InvalidUrl_Returns400()
    {
        var body = new { characterUrl = "not-a-valid-dndbeyond-url" };
        var response = await _client.PostAsJsonAsync("/api/characters/import/dndbeyond-url", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Enter a valid D&D Beyond character URL or ID.",
            json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_NeverLogsTheCharacterUrl()
    {
        StubCharacterResponse(200, DdbCharacterJson);

        const string sensitiveUrl = "https://www.dndbeyond.com/characters/1234567";
        var body = new { characterUrl = sensitiveUrl };
        var response = await _client.PostAsJsonAsync("/api/characters/import/dndbeyond-url", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.DoesNotContain(_logs.Messages, m => m.Contains(sensitiveUrl, StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Messages, m => m.Contains("dndbeyond.com/characters", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Factory that points the "DndBeyond" typed client at the WireMock server.</summary>
    private sealed class DndBeyondWebApplicationFactory : AircaneWebApplicationFactory
    {
        private readonly string _baseAddress;
        private readonly ILoggerProvider _loggerProvider;

        public DndBeyondWebApplicationFactory(string baseAddress, ILoggerProvider loggerProvider)
        {
            _baseAddress = baseAddress;
            _loggerProvider = loggerProvider;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                // Re-register the typed client so its BaseAddress points at WireMock and the timeout
                // is short enough for the timeout test to run quickly. The later configure action
                // runs last and overrides the production values.
                services.AddHttpClient<Aircane.Application.Abstractions.IDndBeyondUrlImportService,
                    Aircane.Infrastructure.Characters.DndBeyondUrlImportService>(c =>
                {
                    c.BaseAddress = new Uri(_baseAddress);
                    c.Timeout = TimeSpan.FromSeconds(2);
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("Aircane-Tabletop/1.0");
                });

                services.AddLogging(logging =>
                {
                    logging.AddProvider(_loggerProvider);
                    logging.SetMinimumLevel(LogLevel.Trace);
                });
            });
        }
    }

    /// <summary>Captures every formatted log message for the no-URL-in-logs assertion.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentBag<string> _messages;

            public CapturingLogger(ConcurrentBag<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _messages.Add(formatter(state, exception));
                if (exception is not null)
                    _messages.Add(exception.ToString());
            }
        }
    }
}
