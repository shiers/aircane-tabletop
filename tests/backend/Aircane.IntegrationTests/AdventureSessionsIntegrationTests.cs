using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// End-to-end tests of the solo adventure endpoints (<c>/api/adventure-sessions</c>) against the
/// real pipeline with the in-memory database and the fake AI provider.
/// </summary>
public class AdventureSessionsIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private const string BaseUrl = "/api/adventure-sessions";

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AdventureSessionsIntegrationTests(AircaneWebApplicationFactory factory)
    {
        // The AI settings service loads <parent of Storage:DocumentsPath>/ai-settings.json, and the
        // Web SDK copies a developer's local copy of that file into the test output. Point storage at
        // an empty temp dir so these tests always use the fake AI provider and never call a real one.
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DocumentsPath"] = Path.Combine(Path.GetTempPath(), $"aircane-it-{Guid.NewGuid():N}", "documents"),
            })));
        _client = _factory.CreateClient();
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    private async Task<(Guid CampaignId, Guid AdventureId)> SeedAsync(
        GeneratedAdventureStatus status = GeneratedAdventureStatus.Approved)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();

        var campaign = new Campaign("Solo Campaign", "D&D 5e 2014", "PHB", AiRole.FullDm, AiAuthority.FullSessionControl);
        var adventure = BuildAdventure(status);
        db.Campaigns.Add(campaign);
        db.GeneratedAdventures.Add(adventure);
        await db.SaveChangesAsync();
        return (campaign.Id, adventure.Id);
    }

    private static GeneratedAdventure BuildAdventure(GeneratedAdventureStatus status)
    {
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "The Sunken Library", Description = "Flooded shelves.", SceneType = "exploration", ConnectsTo = ["scene-2"], Objectives = ["Find the tome"] },
            new() { SceneId = "scene-2", Title = "The Grand Hall", Description = "Cracked marble.", SceneType = "combat", ConnectsTo = ["scene-3"] },
            new() { SceneId = "scene-3", Title = "The Hidden Passage", Description = "A narrow tunnel.", SceneType = "roleplay", ConnectsTo = [] },
        };
        var npcs = new List<GeneratedNpc>
        {
            new() { Name = "Archivist", Role = "ally", Personality = "Timid", StatsSummary = "Commoner", SceneIds = ["scene-1"] },
        };
        var clues = new AdventureClues
        {
            Secrets = [],
            Handouts = [new AdventureHandout { Title = "The torn letter", Content = "Meet me below.", SceneId = "scene-1" }],
            FailForwardPaths = [],
        };

        var now = DateTime.UtcNow;
        return new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "The Sunken Archive",
            Status = status,
            RequestJson = "{}",
            PitchJson = JsonSerializer.Serialize(new AdventurePitch { Title = "The Sunken Archive", Hook = "h", Summary = "s" }, CamelCase),
            ScenesJson = JsonSerializer.Serialize(scenes, CamelCase),
            NpcsJson = JsonSerializer.Serialize(npcs, CamelCase),
            CluesJson = JsonSerializer.Serialize(clues, CamelCase),
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<AdventureSessionDto> StartAsync(Guid campaignId, Guid adventureId)
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, new { generatedAdventureId = adventureId, campaignId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdventureSessionDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    // ── POST /api/adventure-sessions ──────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_PostSession_CreatesAndReturnsSession()
    {
        var (campaignId, adventureId) = await SeedAsync();

        var dto = await StartAsync(campaignId, adventureId);

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(adventureId, dto.GeneratedAdventureId);
        Assert.Equal(campaignId, dto.CampaignId);
        Assert.Equal("scene-1", dto.CurrentSceneId);
        Assert.Equal("InProgress", dto.Status);
        // A solo play session is created so SignalR events have a group to go to.
        Assert.NotNull(dto.SessionId);

        // Starting again resumes the same run instead of creating another.
        var again = await StartAsync(campaignId, adventureId);
        Assert.Equal(dto.Id, again.Id);
        Assert.Equal(dto.SessionId, again.SessionId);
    }

    [Fact]
    public async Task AdventureSessionsController_PostSession_DraftAdventure_ReturnsBadRequest()
    {
        var (campaignId, adventureId) = await SeedAsync(GeneratedAdventureStatus.Draft);

        var response = await _client.PostAsJsonAsync(BaseUrl, new { generatedAdventureId = adventureId, campaignId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdventureSessionsController_PostSession_UnknownAdventure_ReturnsNotFound()
    {
        var (campaignId, _) = await SeedAsync();

        var response = await _client.PostAsJsonAsync(BaseUrl, new { generatedAdventureId = Guid.NewGuid(), campaignId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdventureSessionsController_PostSession_DifferentAdventureInProgress_ReturnsConflict()
    {
        var (campaignId, adventureId) = await SeedAsync();
        await StartAsync(campaignId, adventureId);

        Guid otherAdventureId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            var other = BuildAdventure(GeneratedAdventureStatus.Approved);
            db.GeneratedAdventures.Add(other);
            await db.SaveChangesAsync();
            otherAdventureId = other.Id;
        }

        var response = await _client.PostAsJsonAsync(BaseUrl, new { generatedAdventureId = otherAdventureId, campaignId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AdventureSessionsController_PostSession_MissingIds_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, new { generatedAdventureId = Guid.Empty, campaignId = Guid.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GET /{id} and /active ─────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_GetSession_ReturnsFullState()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var state = await _client.GetFromJsonAsync<AdventureSessionStateDto>($"{BaseUrl}/{started.Id}");

        Assert.NotNull(state);
        Assert.Equal("The Sunken Archive", state!.AdventureTitle);
        Assert.Equal("scene-1", state.CurrentScene.SceneId);
        Assert.Equal(["Find the tome"], state.CurrentScene.Objectives);
        Assert.Equal("scene-2", Assert.Single(state.CurrentScene.Exits).SceneId);
        Assert.Equal("Archivist", Assert.Single(state.CurrentScene.Npcs).NpcName);
        Assert.Equal(1, state.CurrentSceneNumber);
        Assert.Equal(3, state.TotalScenes);
    }

    [Fact]
    public async Task AdventureSessionsController_GetSession_Unknown_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdventureSessionsController_GetActiveSession_ReturnsExistingSession()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var active = await _client.GetFromJsonAsync<AdventureSessionDto>($"{BaseUrl}/active?campaignId={campaignId}");

        Assert.NotNull(active);
        Assert.Equal(started.Id, active!.Id);
    }

    [Fact]
    public async Task AdventureSessionsController_GetActiveSession_NoneStarted_ReturnsNotFound()
    {
        var (campaignId, _) = await SeedAsync();

        var response = await _client.GetAsync($"{BaseUrl}/active?campaignId={campaignId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── advance-scene / available-scenes ──────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_AdvanceScene_UpdatesCurrentScene()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var response = await _client.PostAsJsonAsync(
            $"{BaseUrl}/{started.Id}/advance-scene",
            new { targetSceneId = "scene-2", transitionNarration = "You wade into the hall." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<AdventureSessionStateDto>();
        Assert.Equal("scene-2", state!.CurrentScene.SceneId);
        Assert.Equal(["scene-1"], state.CompletedSceneIds);
        Assert.Equal(1, state.CompletedSceneCount);

        var active = await _client.GetFromJsonAsync<AdventureSessionDto>($"{BaseUrl}/active?campaignId={campaignId}");
        Assert.Equal("scene-2", active!.CurrentSceneId);
    }

    [Fact]
    public async Task AdventureSessionsController_AdvanceScene_UnreachableScene_ReturnsBadRequest()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var response = await _client.PostAsJsonAsync(
            $"{BaseUrl}/{started.Id}/advance-scene", new { targetSceneId = "scene-3" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("not reachable", problem!.Detail);
    }

    [Fact]
    public async Task AdventureSessionsController_AvailableScenes_ReturnsReachableScenes()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var exits = await _client.GetFromJsonAsync<List<AdventureExitDto>>($"{BaseUrl}/{started.Id}/available-scenes");

        var exit = Assert.Single(exits!);
        Assert.Equal("scene-2", exit.SceneId);
        Assert.Equal("The Grand Hall", exit.Title);
    }

    // ── abandon ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_Abandon_SetsStatusAbandoned()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var response = await _client.PostAsync($"{BaseUrl}/{started.Id}/abandon", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<AdventureSessionStateDto>();
        Assert.Equal("Abandoned", state!.Status);
        Assert.NotNull(state.CompletedAt);

        var active = await _client.GetAsync($"{BaseUrl}/active?campaignId={campaignId}");
        Assert.Equal(HttpStatusCode.NotFound, active.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        Assert.Equal(AdventureSessionStatus.Abandoned,
            (await db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == started.Id)).Status);
    }

    // ── player-action ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_PlayerAction_ReturnsNarrationAndFreshState()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var response = await _client.PostAsJsonAsync(
            $"{BaseUrl}/{started.Id}/player-action", new { actionText = "I search the shelves" });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<AdventureSessionActionResponse>();
        Assert.NotNull(body);
        // The fake AI provider echoes the last user message; the solo run has no character tag.
        Assert.Contains("Player action: I search the shelves", body!.Narration);
        Assert.DoesNotContain("[Character", body.Narration);
        Assert.Equal("scene-1", body.State.CurrentScene.SceneId);
        Assert.Equal(started.Id, body.State.Id);
    }

    [Fact]
    public async Task AdventureSessionsController_PlayerAction_EmptyText_ReturnsBadRequest()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{started.Id}/player-action", new { actionText = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdventureSessionsController_PlayerAction_AbandonedRun_ReturnsBadRequest()
    {
        var (campaignId, adventureId) = await SeedAsync();
        var started = await StartAsync(campaignId, adventureId);
        await _client.PostAsync($"{BaseUrl}/{started.Id}/abandon", content: null);

        var response = await _client.PostAsJsonAsync(
            $"{BaseUrl}/{started.Id}/player-action", new { actionText = "Hello?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
