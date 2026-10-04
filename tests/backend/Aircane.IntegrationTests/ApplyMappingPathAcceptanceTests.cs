using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Characters.Import;
using Aircane.Workers.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// MEDIUM-2 + NIT-3 guard: every path a mapper may emit (<see cref="CanonicalCharacterPaths.Supported"/>)
/// must be accepted by the save-time <c>ApplyMapping</c> switch, with a representative value that is
/// in range for that path. A bogus path must still be rejected, and a successful apply must land the
/// value in the expected <c>CanonicalJson</c> location.
/// </summary>
public class ApplyMappingPathAcceptanceTests
    : IClassFixture<AircaneWebApplicationFactory>, IAsyncLifetime
{
    private readonly AircaneWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const string MinimalCharacterJson =
        """
        {
          "identity": { "name": "Draft Hero" },
          "classes": [ { "className": "Fighter", "level": 1, "hitDie": 10 } ],
          "abilities": { "strength": 10, "dexterity": 10, "constitution": 10, "intelligence": 10, "wisdom": 10, "charisma": 10 },
          "combat": { "armorClass": 10, "speed": 30, "maxHitPoints": 10, "currentHitPoints": 10 }
        }
        """;

    public ApplyMappingPathAcceptanceTests(AircaneWebApplicationFactory factory)
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

    /// <summary>Representative, range-safe value for a given canonical path (lowercased).</summary>
    private static string RepresentativeValue(string path)
    {
        return path switch
        {
            "abilities.strength" or "strength" => "15",
            "abilities.dexterity" or "dexterity" => "15",
            "abilities.constitution" or "constitution" => "15",
            "abilities.intelligence" or "intelligence" => "15",
            "abilities.wisdom" or "wisdom" => "15",
            "abilities.charisma" or "charisma" => "15",
            "combat.armorclass" or "armorclass" => "15",
            "combat.hitpoints" or "combat.currenthitpoints" or "hitpoints" => "20",
            "combat.maxhitpoints" or "maxhitpoints" => "20",
            "combat.speed" or "speed" => "30",
            "combat.initiative" or "initiative" => "2",
            "combat.proficiencybonus" or "proficiencybonus" => "2",
            "level" => "1",
            // all string paths
            _ => "x",
        };
    }

    public static IEnumerable<object[]> SupportedPaths()
        => CanonicalCharacterPaths.Supported.Select(p => new object[] { p });

    private async Task<Guid> CreateDraftAsync()
    {
        var create = new
        {
            name = "Draft Hero",
            gameSystem = "D&D 5e",
            ruleset = "2014",
            level = 1,
            canonicalJson = MinimalCharacterJson,
        };

        var response = await _client.PostAsJsonAsync("/api/characters", create);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    [Theory]
    [MemberData(nameof(SupportedPaths))]
    public async Task ApplyMapping_AcceptsEverySupportedPath(string path)
    {
        var id = await CreateDraftAsync();

        var request = new
        {
            mappings = new[]
            {
                new { sourceFieldName = path, canonicalFieldPath = path, value = RepresentativeValue(path) }
            }
        };

        var response = await _client.PutAsJsonAsync($"/api/characters/{id}/field-mappings", request);

        if ((int)response.StatusCode is >= 400 and < 500)
        {
            var detail = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Unknown canonical field path", detail);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApplyMapping_RejectsBogusPath_WithUnknownFieldMessage()
    {
        var id = await CreateDraftAsync();

        var request = new
        {
            mappings = new[]
            {
                new { sourceFieldName = "not.a.real.path", canonicalFieldPath = "not.a.real.path", value = "x" }
            }
        };

        var response = await _client.PutAsJsonAsync($"/api/characters/{id}/field-mappings", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var detail = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown canonical field path", detail);
    }

    [Fact]
    public async Task ApplyMapping_RoundTrips_ValueIntoExpectedCanonicalLocation()
    {
        var id = await CreateDraftAsync();

        var request = new
        {
            mappings = new[]
            {
                new { sourceFieldName = "abilities.strength", canonicalFieldPath = "abilities.strength", value = "18" }
            }
        };

        var apply = await _client.PutAsJsonAsync($"/api/characters/{id}/field-mappings", request);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);

        var get = await _client.GetAsync($"/api/characters/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var canonicalJson = json.RootElement.GetProperty("canonicalJson").GetString();
        Assert.NotNull(canonicalJson);

        using var canonical = JsonDocument.Parse(canonicalJson!);
        var strength = canonical.RootElement
            .GetProperty("abilities")
            .GetProperty("strength")
            .GetInt32();
        Assert.Equal(18, strength);
    }
}
