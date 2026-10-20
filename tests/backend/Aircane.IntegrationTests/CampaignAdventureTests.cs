using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Attaching an adventure to a campaign, taking it out again, and deleting it, through the token-validating
/// pipeline (desktop mode: the host is the local machine, a player carries a real participant token).
/// </summary>
public sealed class CampaignAdventureTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string Dnd = "D&D 5e 2014";

    private readonly DesktopModeWebApplicationFactory _factory;

    public CampaignAdventureTests(DesktopModeWebApplicationFactory factory) => _factory = factory;

    // ── Set-up ────────────────────────────────────────────────────────────────

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private async Task<CampaignDto> CreateCampaignAsync(string name, string gameSystem = Dnd)
    {
        using var host = Host();
        return (await (await host.PostAsJsonAsync("/api/campaigns", new CreateCampaignRequest(name, gameSystem, "PHB")))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<CampaignDto>())!;
    }

    /// <summary>An adventure with an indexed document and chunks, optionally in a campaign.</summary>
    private async Task<Guid> SeedAdventureAsync(
        Guid? campaignId = null, string gameSystem = Dnd, string title = "The Cellar Below", int chunks = 3,
        GeneratedAdventureStatus status = GeneratedAdventureStatus.Approved)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var now = DateTime.UtcNow;
        var adventure = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = status,
            RequestJson = JsonSerializer.Serialize(new { mode = "Group", ruleset = gameSystem, gameSystem, averageLevel = 3, length = "short", tone = "epic", difficulty = "medium" }, Web),
            ScenesJson = "[]",
            Ruleset = gameSystem,
            GameSystem = gameSystem,
            CampaignId = campaignId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedAdventures.Add(adventure);

        var document = new SourceDocument(
            title, $"generated-adventure-{adventure.Id}.json", SourceType.Generated, SourceMode.Upload,
            gameSystem, gameSystem, $"generated://{adventure.Id}", importStatus: ImportStatus.Completed);
        db.SourceDocuments.Add(document);
        for (var i = 0; i < chunks; i++)
            db.DocumentChunks.Add(new DocumentChunk(document.Id, i, $"chunk {i}"));

        await db.SaveChangesAsync();
        return adventure.Id;
    }

    /// <summary>A run of the adventure in the given campaign, with some transcript entries.</summary>
    private async Task<Guid> SeedRunAsync(Guid adventureId, Guid campaignId, AdventureSessionStatus status, int entries = 2)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var now = DateTimeOffset.UtcNow;
        var run = new AdventureSession
        {
            Id = Guid.NewGuid(),
            GeneratedAdventureId = adventureId,
            CampaignId = campaignId,
            CurrentSceneId = "scene-1",
            Status = status,
            StartedAt = now,
            LastActivityAt = now,
        };
        db.AdventureSessions.Add(run);
        for (var i = 0; i < entries; i++)
        {
            db.AdventureSessionEntries.Add(new AdventureSessionEntry
            {
                Id = Guid.NewGuid(),
                AdventureSessionId = run.Id,
                EntryType = i % 2 == 0 ? AdventureEntryType.PlayerAction : AdventureEntryType.AiNarration,
                Content = $"entry {i}",
                CreatedAt = now.AddSeconds(i),
            });
        }
        await db.SaveChangesAsync();
        return run.Id;
    }

    private async Task<T> InDbAsync<T>(Func<AircaneDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AircaneDbContext>());
    }

    private Task<GeneratedAdventure?> AdventureAsync(Guid id)
        => InDbAsync(db => db.GeneratedAdventures.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id));

    private Task<int> CountAsync(Guid adventureId, Func<AircaneDbContext, Guid, Task<int>> count)
        => InDbAsync(db => count(db, adventureId));

    private Task<int> DocumentCountAsync(Guid id)
        => InDbAsync(db => db.SourceDocuments.CountAsync(d => d.SourcePath == $"generated://{id}"));

    private Task<int> ChunkCountAsync(Guid id)
        => InDbAsync(db => db.DocumentChunks.CountAsync(c =>
            db.SourceDocuments.Any(d => d.Id == c.SourceDocumentId && d.SourcePath == $"generated://{id}")));

    private Task<int> RunCountAsync(Guid id)
        => InDbAsync(db => db.AdventureSessions.CountAsync(r => r.GeneratedAdventureId == id));

    private Task<int> EntryCountAsync(Guid id)
        => InDbAsync(db => db.AdventureSessionEntries.CountAsync(e =>
            db.AdventureSessions.Any(r => r.Id == e.AdventureSessionId && r.GeneratedAdventureId == id)));

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> AssignAsync(
        HttpClient client, Guid adventureId, Guid campaignId, bool confirmMove = false, bool confirmMismatch = false)
        => client.PatchAsJsonAsync($"/api/adventures/{adventureId}/assign", new { campaignId, confirmMove, confirmMismatch });

    // ═════════════════════════════════════════════════════════════════════════
    // 1. Delete
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_WithNoActiveRun_RemovesTheAdventure_ItsDocument_ChunksRunsAndTranscripts()
    {
        var campaign = await CreateCampaignAsync("Delete me");
        var id = await SeedAdventureAsync(campaign.Id);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.Completed);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.Abandoned);
        using var host = Host();

        using var response = await host.DeleteAsync($"/api/adventures/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await AdventureAsync(id));
        Assert.Equal(0, await DocumentCountAsync(id));
        Assert.Equal(0, await ChunkCountAsync(id));
        Assert.Equal(0, await RunCountAsync(id));
        Assert.Equal(0, await EntryCountAsync(id));
    }

    [Fact]
    public async Task Delete_WhileARunIsInProgress_IsRefused_AndNothingIsRemoved()
    {
        var campaign = await CreateCampaignAsync("Busy");
        var id = await SeedAdventureAsync(campaign.Id);
        var runId = await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.InProgress);
        using var host = Host();

        using var response = await host.DeleteAsync($"/api/adventures/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("adventure-run-in-progress", problem.GetProperty("code").GetString());
        Assert.Contains("in progress", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await AdventureAsync(id));
        Assert.Equal(1, await DocumentCountAsync(id));
        Assert.Equal(3, await ChunkCountAsync(id));
        Assert.Equal(1, await RunCountAsync(id));
        Assert.Equal(2, await EntryCountAsync(id));
        _ = runId;
    }

    [Fact]
    public async Task Delete_OfAnUnknownAdventure_Is404()
    {
        using var host = Host();
        using var response = await host.DeleteAsync($"/api/adventures/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. Delete impact
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteImpact_CountsRunsByStatus_Transcripts_AndIndexedContent()
    {
        var campaign = await CreateCampaignAsync("Impact");
        var id = await SeedAdventureAsync(campaign.Id, chunks: 4);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.Completed, entries: 3);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.Abandoned, entries: 1);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.InProgress, entries: 2);
        using var host = Host();

        var impact = await host.GetFromJsonAsync<AdventureDeleteImpactDto>($"/api/adventures/{id}/delete-impact", Web);

        Assert.NotNull(impact);
        Assert.Equal(id, impact.Id);
        Assert.Equal("The Cellar Below", impact.Title);
        Assert.Equal(3, impact.TotalRuns);
        Assert.Equal(1, impact.InProgressRuns);
        Assert.Equal(1, impact.CompletedRuns);
        Assert.Equal(1, impact.AbandonedRuns);
        Assert.Equal(0, impact.NotStartedRuns);
        Assert.Equal(6, impact.TranscriptEntries);
        Assert.Equal(4, impact.IndexedChunks);
    }

    [Fact]
    public async Task DeleteImpact_OfAnUnknownAdventure_Is404()
    {
        using var host = Host();
        using var response = await host.GetAsync($"/api/adventures/{Guid.NewGuid()}/delete-impact");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. Unassign
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Unassign_ClearsTheCampaign_AndKeepsEverythingElse()
    {
        var campaign = await CreateCampaignAsync("Leaving");
        var id = await SeedAdventureAsync(campaign.Id);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.Completed, entries: 3);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.Abandoned, entries: 1);
        using var host = Host();

        using var response = await host.PatchAsync($"/api/adventures/{id}/unassign", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<AdventureListItemDto>(Web);
        Assert.Null(item!.CampaignId);
        Assert.Null(item.CampaignName);

        var adventure = await AdventureAsync(id);
        Assert.NotNull(adventure);
        Assert.Null(adventure.CampaignId);
        Assert.Equal(1, await DocumentCountAsync(id));
        Assert.Equal(3, await ChunkCountAsync(id));
        Assert.Equal(2, await RunCountAsync(id));
        Assert.Equal(4, await EntryCountAsync(id));

        // The finished runs keep the campaign they were played in.
        var runCampaigns = await InDbAsync(db => db.AdventureSessions.Where(r => r.GeneratedAdventureId == id)
            .Select(r => r.CampaignId).ToListAsync());
        Assert.All(runCampaigns, c => Assert.Equal(campaign.Id, c));
    }

    [Fact]
    public async Task Unassign_WhileARunIsInProgress_IsRefused_AndTheLinkStays()
    {
        var campaign = await CreateCampaignAsync("Playing");
        var id = await SeedAdventureAsync(campaign.Id);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.InProgress);
        using var host = Host();

        using var response = await host.PatchAsync($"/api/adventures/{id}/unassign", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("adventure-run-in-progress", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(campaign.Id, (await AdventureAsync(id))!.CampaignId);
    }

    [Fact]
    public async Task Unassign_OfAnAdventureWithNoCampaign_IsHarmless()
    {
        var id = await SeedAdventureAsync();
        using var host = Host();

        using var response = await host.PatchAsync($"/api/adventures/{id}/unassign", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await AdventureAsync(id));
    }

    [Fact]
    public async Task Unassign_OfAnUnknownAdventure_Is404()
    {
        using var host = Host();
        using var response = await host.PatchAsync($"/api/adventures/{Guid.NewGuid()}/unassign", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4. Assign
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_AnUnassignedAdventureOfTheSameSystem_Works_WithNoConfirmation()
    {
        var campaign = await CreateCampaignAsync("Home");
        var id = await SeedAdventureAsync();
        using var host = Host();

        using var response = await AssignAsync(host, id, campaign.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<AdventureListItemDto>(Web);
        Assert.Equal(campaign.Id, item!.CampaignId);
        Assert.Equal("Home", item.CampaignName);
        Assert.Equal(campaign.Id, (await AdventureAsync(id))!.CampaignId);
    }

    [Fact]
    public async Task Assign_ToACampaignOfAnotherGameSystem_NeedsConfirmation()
    {
        var campaign = await CreateCampaignAsync("Pathfinders", "Pathfinder 2e");
        var id = await SeedAdventureAsync(gameSystem: Dnd);
        using var host = Host();

        using var refused = await AssignAsync(host, id, campaign.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await ProblemAsync(refused);
        Assert.Equal("game-system-mismatch", problem.GetProperty("code").GetString());
        Assert.Equal(Dnd, problem.GetProperty("adventureGameSystem").GetString());
        Assert.Equal("Pathfinder 2e", problem.GetProperty("campaignGameSystem").GetString());
        Assert.Null((await AdventureAsync(id))!.CampaignId);

        using var confirmed = await AssignAsync(host, id, campaign.Id, confirmMismatch: true);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(campaign.Id, (await AdventureAsync(id))!.CampaignId);
    }

    [Theory]
    [InlineData("d&d 5e 2014 ")]
    [InlineData("  D&D 5E 2014")]
    public async Task Assign_ComparesGameSystems_IgnoringCaseAndSurroundingSpace(string campaignSystem)
    {
        var campaign = await CreateCampaignAsync("Spelling", campaignSystem);
        var id = await SeedAdventureAsync(gameSystem: Dnd);
        using var host = Host();

        using var response = await AssignAsync(host, id, campaign.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Assign_WhenOneSideHasNoGameSystem_IsNotAMismatch()
    {
        var campaign = await CreateCampaignAsync("Blank", "Pathfinder 2e");
        var id = await SeedAdventureAsync(gameSystem: "  ");
        using var host = Host();

        using var response = await AssignAsync(host, id, campaign.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Assign_AnAdventureOfAnotherCampaign_NeedsConfirmation_ThenMoves()
    {
        var from = await CreateCampaignAsync("From");
        var to = await CreateCampaignAsync("To");
        var id = await SeedAdventureAsync(from.Id);
        using var host = Host();

        using var refused = await AssignAsync(host, id, to.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await ProblemAsync(refused);
        Assert.Equal("adventure-in-other-campaign", problem.GetProperty("code").GetString());
        Assert.Equal("From", problem.GetProperty("currentCampaignName").GetString());
        Assert.Equal(from.Id, problem.GetProperty("currentCampaignId").GetGuid());
        Assert.Equal(from.Id, (await AdventureAsync(id))!.CampaignId);

        using var confirmed = await AssignAsync(host, id, to.Id, confirmMove: true);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(to.Id, (await AdventureAsync(id))!.CampaignId);
    }

    [Fact]
    public async Task Assign_MovingAnAdventureWithARunInProgress_IsRefused_EvenWhenConfirmed()
    {
        var from = await CreateCampaignAsync("Running");
        var to = await CreateCampaignAsync("Waiting");
        var id = await SeedAdventureAsync(from.Id);
        await SeedRunAsync(id, from.Id, AdventureSessionStatus.InProgress);
        using var host = Host();

        using var response = await AssignAsync(host, id, to.Id, confirmMove: true, confirmMismatch: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("adventure-run-in-progress", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(from.Id, (await AdventureAsync(id))!.CampaignId);
    }

    [Fact]
    public async Task Assign_ToTheCampaignItIsAlreadyIn_IsHarmless()
    {
        var campaign = await CreateCampaignAsync("Same");
        var id = await SeedAdventureAsync(campaign.Id);
        await SeedRunAsync(id, campaign.Id, AdventureSessionStatus.InProgress);
        using var host = Host();

        using var response = await AssignAsync(host, id, campaign.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Assign_WithAnUnknownCampaignOrAdventure_Is404()
    {
        var campaign = await CreateCampaignAsync("Real");
        var id = await SeedAdventureAsync();
        using var host = Host();

        using var noCampaign = await AssignAsync(host, id, Guid.NewGuid());
        using var noAdventure = await AssignAsync(host, Guid.NewGuid(), campaign.Id);

        Assert.Equal(HttpStatusCode.NotFound, noCampaign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noAdventure.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5. Listing
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListAll_CarriesTheCampaignAndItsName_ForAssignedAdventures()
    {
        var campaign = await CreateCampaignAsync("Listed");
        var assigned = await SeedAdventureAsync(campaign.Id, title: "Assigned one");
        var loose = await SeedAdventureAsync(title: "Loose one");
        using var host = Host();

        var all = (await host.GetFromJsonAsync<List<AdventureListItemDto>>("/api/adventures", Web))!;

        var assignedItem = Assert.Single(all, a => a.Id == assigned);
        Assert.Equal(campaign.Id, assignedItem.CampaignId);
        Assert.Equal("Listed", assignedItem.CampaignName);
        Assert.Equal("Group", assignedItem.Mode);
        var looseItem = Assert.Single(all, a => a.Id == loose);
        Assert.Null(looseItem.CampaignId);
        Assert.Null(looseItem.CampaignName);

        var unassigned = (await host.GetFromJsonAsync<List<AdventureListItemDto>>("/api/adventures?unassigned=true", Web))!;
        Assert.Contains(unassigned, a => a.Id == loose);
        Assert.DoesNotContain(unassigned, a => a.Id == assigned);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 6. Who may
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task APlayer_CannotUnassign_Assign_Delete_OrReadTheImpact()
    {
        var campaign = await CreateCampaignAsync("Table");
        var id = await SeedAdventureAsync(campaign.Id);
        var other = await CreateCampaignAsync("Elsewhere");
        var token = await JoinAsPlayerAsync(campaign.Id);
        using var player = _factory.CreateClientFrom(LanAddress);
        player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var unassign = await player.PatchAsync($"/api/adventures/{id}/unassign", null);
        using var assign = await AssignAsync(player, id, other.Id, confirmMove: true);
        using var delete = await player.DeleteAsync($"/api/adventures/{id}");
        using var impact = await player.GetAsync($"/api/adventures/{id}/delete-impact");

        Assert.Equal(HttpStatusCode.Forbidden, unassign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, impact.StatusCode);
        var adventure = await AdventureAsync(id);
        Assert.Equal(campaign.Id, adventure!.CampaignId);
    }

    [Fact]
    public async Task AnAnonymousCaller_FromTheNetwork_IsRefused()
    {
        var id = await SeedAdventureAsync();
        using var stranger = _factory.CreateClientFrom(LanAddress);

        using var unassign = await stranger.PatchAsync($"/api/adventures/{id}/unassign", null);

        Assert.True(unassign.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    private async Task<string> JoinAsPlayerAsync(Guid campaignId)
    {
        using var host = Host();
        var session = (await (await host.PostAsJsonAsync($"/api/campaigns/{campaignId}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = false }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>())!;
        using var lan = _factory.CreateClientFrom(LanAddress);
        var joined = (await (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = "Alice", InviteCode = session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
        return joined.ParticipantToken;
    }
}
