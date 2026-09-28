using System.Net;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// End-to-end test of a full combat round through the combat REST endpoints:
/// start encounter → roll initiative → advance turn → apply damage → death save → end encounter.
/// Verifies the encounter state stays consistent throughout.
/// </summary>
public class CombatIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly AircaneWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CombatIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullCombatRound_KeepsEncounterStateConsistent()
    {
        var (sessionId, _) = await SeedSessionAsync();

        // 1. Start the encounter with two combatants (initiative not yet rolled).
        var start = await Post<EncounterStateDto>(
            $"/api/sessions/{sessionId}/combat/start",
            new
            {
                combatants = new[]
                {
                    new { id = "hero", name = "Hero", isPlayerCharacter = true, currentHp = 20, maxHp = 20 },
                    new { id = "goblin", name = "Goblin", isPlayerCharacter = false, currentHp = 7, maxHp = 7 },
                },
            });
        Assert.NotNull(start);
        Assert.False(start!.IsActive); // not active until initiative rolled
        Assert.Equal(2, start.Combatants.Count);

        // 2. Roll initiative — encounter becomes active, ordered highest-first.
        var rolled = await Post<EncounterStateDto>(
            $"/api/sessions/{sessionId}/combat/roll-initiative",
            new { initiative = new Dictionary<string, int> { ["hero"] = 18, ["goblin"] = 12 } });
        Assert.NotNull(rolled);
        Assert.True(rolled!.IsActive);
        Assert.Equal(1, rolled.Round);
        Assert.Equal("hero", rolled.ActiveCombatantId);
        Assert.Equal(new[] { "hero", "goblin" }, rolled.InitiativeOrder.ToArray());

        // 3. Advance turn → goblin is active.
        var advanced = await Post<EncounterStateDto>($"/api/sessions/{sessionId}/combat/advance-turn", null);
        Assert.NotNull(advanced);
        Assert.Equal("goblin", advanced!.ActiveCombatantId);

        // 4. Apply lethal damage to the goblin.
        var damaged = await Post<EncounterStateDto>(
            $"/api/sessions/{sessionId}/combat/apply-damage",
            new { targetId = "goblin", amount = 10 });
        Assert.NotNull(damaged);
        var goblin = damaged!.Combatants.Single(c => c.Id == "goblin");
        Assert.True(goblin.CurrentHp <= 0);
        Assert.True(goblin.IsDowned);

        // 5. Apply damage to the hero to down them, then record a failed death save.
        await Post<EncounterStateDto>(
            $"/api/sessions/{sessionId}/combat/apply-damage",
            new { targetId = "hero", amount = 25 });
        var afterDeathSave = await Post<EncounterStateDto>(
            $"/api/sessions/{sessionId}/combat/death-save",
            new { targetId = "hero", success = false });
        Assert.NotNull(afterDeathSave);
        var hero = afterDeathSave!.Combatants.Single(c => c.Id == "hero");
        Assert.NotNull(hero.DeathSaves);
        Assert.Equal(1, hero.DeathSaves!.Failures);

        // 6. End the encounter — no longer active.
        var ended = await Post<EncounterStateDto>($"/api/sessions/{sessionId}/combat/end", null);
        Assert.NotNull(ended);
        Assert.False(ended!.IsActive);

        // 7. GET reflects the final (inactive) encounter.
        var getResponse = await _client.GetAsync($"/api/sessions/{sessionId}/combat");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var current = await getResponse.Content.ReadFromJsonAsync<EncounterStateDto>();
        Assert.NotNull(current);
        Assert.False(current!.IsActive);
    }

    [Fact]
    public async Task GetEncounter_NoEncounter_Returns204()
    {
        var (sessionId, _) = await SeedSessionAsync();
        var response = await _client.GetAsync($"/api/sessions/{sessionId}/combat");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ApplyDamage_NegativeAmount_Returns400()
    {
        var (sessionId, _) = await SeedSessionAsync();
        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/combat/apply-damage",
            new { targetId = "hero", amount = -5 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid SessionId, Guid CampaignId)> SeedSessionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();

        var campaign = new Aircane.Domain.Entities.Campaign(
            "Combat Test", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        db.Campaigns.Add(campaign);

        var session = new Aircane.Domain.Entities.Session(
            campaign.Id, "Combat Session", SessionAccessMode.Solo, "test-hash", SessionStatus.Active);
        db.Sessions.Add(session);

        await db.SaveChangesAsync();
        return (session.Id, campaign.Id);
    }

    private async Task<T?> Post<T>(string url, object? body)
    {
        var response = body is null
            ? await _client.PostAsync(url, content: null)
            : await _client.PostAsJsonAsync(url, body);

        Assert.True(
            response.IsSuccessStatusCode,
            $"POST {url} returned {(int)response.StatusCode} {response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());

        if (response.StatusCode == HttpStatusCode.NoContent)
            return default;

        return await response.Content.ReadFromJsonAsync<T>();
    }
}
