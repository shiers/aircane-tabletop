using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aircane.Workers.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the source-adapter character import path (FEAT-001). Covers detection,
/// the MEDIUM-1 legacy routing rule, malformed-JSON / payload-size guards, and the
/// unrecognised-format prompt.
/// </summary>
public class CharacterSourceImportIntegrationTests
    : IClassFixture<AircaneWebApplicationFactory>, IAsyncLifetime
{
    private readonly AircaneWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const string PathbuilderJson =
        """
        {
          "success": true,
          "build": {
            "name": "Test Character",
            "class": "Fighter",
            "level": 5,
            "ancestry": "Dwarf",
            "heritage": "Ancient-Blooded Dwarf",
            "background": "Warrior",
            "abilities": { "str": 18, "dex": 14, "con": 16, "int": 10, "wis": 12, "cha": 8 },
            "attributes": { "hp": 68, "ac": 22, "speed": 20, "classDC": 15 },
            "fortitude": 1,
            "reflex": 1,
            "will": 1,
            "lores": [["Warfare", 1]],
            "feats": [["Power Attack", null, "Class Feat", 1]],
            "equipment": [["Warhammer", 1]]
          }
        }
        """;

    public CharacterSourceImportIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<GameSystemDefinitionSeeder>();
        await seeder.SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Import_DetectsPathbuilder_AndReturnsReviewEnvelope()
    {
        var body = new { canonicalJson = PathbuilderJson };
        var response = await _client.PostAsJsonAsync("/api/characters/import", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("PathbuilderTwo", root.GetProperty("detectedSource").GetString());
        Assert.Equal("high", root.GetProperty("confidence").GetString());

        var review = root.GetProperty("review");
        var gsid = review.GetProperty("gameSystemDefinitionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(gsid));

        var mapped = review.GetProperty("mappedFields");
        Assert.Equal("Test Character", mapped.GetProperty("identity.name").GetString());
        Assert.Equal("18", mapped.GetProperty("abilities.strength").GetString());
        Assert.Equal("22", mapped.GetProperty("combat.armorClass").GetString());
    }

    [Fact]
    public async Task Import_LegacyGameSystemWithoutRuleset_Returns400RulesetRequired()
    {
        // MEDIUM-1 regression: gameSystem present, ruleset absent, no source → LEGACY branch.
        var body = new { canonicalJson = PathbuilderJson, gameSystem = "D&D 5e" };
        var response = await _client.PostAsJsonAsync("/api/characters/import", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ruleset is required.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_MalformedJson_OnAdapterPath_Returns400MalformedJson()
    {
        var body = new { canonicalJson = "{ this is : not valid json" };
        var response = await _client.PostAsJsonAsync("/api/characters/import", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Malformed JSON", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "The uploaded character JSON could not be parsed.",
            json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_PayloadOverTwoMegabytes_Returns400PayloadTooLarge()
    {
        // Build a syntactically valid JSON string just over 2 MB.
        var filler = new string('a', 2 * 1024 * 1024 + 16);
        var oversized = "{\"x\":\"" + filler + "\"}";
        var body = new { canonicalJson = oversized };

        var response = await _client.PostAsJsonAsync("/api/characters/import", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Payload too large", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "Character JSON exceeds the 2 MB limit.",
            json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_UnrecognisedObject_WithNoSource_PromptsForSourceConfirmation()
    {
        var body = new { canonicalJson = "{\"totally\":\"unrelated\"}" };
        var response = await _client.PostAsJsonAsync("/api/characters/import", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("requiresSourceConfirmation").GetBoolean());
    }
}
