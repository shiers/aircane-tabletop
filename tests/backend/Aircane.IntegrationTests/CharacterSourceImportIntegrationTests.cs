using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aircane.Workers.Seeding;
using Microsoft.EntityFrameworkCore;
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

    private const string FoundryDnd5eJson =
        """
        {
          "name": "Test Character",
          "type": "character",
          "_stats": { "systemVersion": "3.3.1" },
          "system": {
            "abilities": {
              "str": { "value": 16 }, "dex": { "value": 14 }, "con": { "value": 15 },
              "int": { "value": 10 }, "wis": { "value": 12 }, "cha": { "value": 8 }
            },
            "attributes": {
              "hp": { "value": 28, "max": 34 },
              "ac": { "value": 17 },
              "movement": { "walk": 30 }
            },
            "details": { "race": "Dwarf", "background": "Soldier", "level": 4 }
          },
          "items": [
            { "type": "class", "name": "Fighter", "system": { "levels": 4 } },
            { "type": "subclass", "name": "Champion" }
          ]
        }
        """;

    private const string Roll20Json =
        """
        {
          "schema_version": 2,
          "character": {
            "name": "Test Character",
            "attribs": [
              { "name": "character_name", "current": "Test Character", "max": "" },
              { "name": "class", "current": "Bard", "max": "" },
              { "name": "level", "current": "3", "max": "" },
              { "name": "dexterity", "current": "16", "max": "" },
              { "name": "hp", "current": "21", "max": "24" },
              { "name": "hp_max", "current": "24", "max": "" },
              { "name": "ac", "current": "14", "max": "" }
            ]
          }
        }
        """;

    [Fact]
    public async Task Import_DetectsFoundryDnd5e_AndReturnsReviewWithGameSystem()
    {
        var body = new { canonicalJson = FoundryDnd5eJson };
        var response = await _client.PostAsJsonAsync("/api/characters/import", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("FoundryDnd5e", root.GetProperty("detectedSource").GetString());

        var review = root.GetProperty("review");
        var gsid = review.GetProperty("gameSystemDefinitionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(gsid));

        var mapped = review.GetProperty("mappedFields");
        Assert.Equal("Test Character", mapped.GetProperty("identity.name").GetString());
        Assert.Equal("Fighter", mapped.GetProperty("class").GetString());
        Assert.Equal("17", mapped.GetProperty("combat.armorClass").GetString());
    }

    [Fact]
    public async Task Import_Roll20_TwoCallHandshake_FirstCallDoesNotPersist_SecondCallDoes()
    {
        // ── First call: no source, no game system → requires selection, persists NOTHING. ──
        var beforeCount = await CountCharactersAsync();

        var firstBody = new { canonicalJson = Roll20Json };
        var firstResponse = await _client.PostAsJsonAsync("/api/characters/import", firstBody);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using (var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync()))
        {
            var root = firstJson.RootElement;
            Assert.True(root.GetProperty("requiresSourceConfirmation").GetBoolean());

            var review = root.GetProperty("review");
            Assert.True(review.GetProperty("requiresGameSystemSelection").GetBoolean());
        }

        Assert.Equal(beforeCount, await CountCharactersAsync());

        // ── Second call: pin ?source=Roll20 AND a valid gameSystemDefinitionId → persists. ──
        var dnd5eId = GameSystemIds.DnD5e2014;
        var secondResponse = await _client.PostAsJsonAsync(
            $"/api/characters/import?source=Roll20&gameSystemDefinitionId={dnd5eId}", firstBody);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var secondRoot = secondJson.RootElement;

        // Detection is pinned by the explicit source, not re-run.
        Assert.Equal("Roll20", secondRoot.GetProperty("detectedSource").GetString());

        var secondReview = secondRoot.GetProperty("review");
        Assert.False(secondReview.GetProperty("requiresGameSystemSelection").GetBoolean());

        var mapped = secondReview.GetProperty("mappedFields");
        Assert.Equal("Bard", mapped.GetProperty("class").GetString());
        Assert.Equal("16", mapped.GetProperty("abilities.dexterity").GetString());

        Assert.Equal(beforeCount + 1, await CountCharactersAsync());
    }

    private async Task<int> CountCharactersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Aircane.Infrastructure.Persistence.AircaneDbContext>();
        return await db.Characters.CountAsync();
    }

    private static class GameSystemIds
    {
        public static readonly Guid DnD5e2014 = Guid.Parse("10000000-0000-0000-0000-000000000001");
    }
}
