using System.Text.Json;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.IntegrationTests.Retrieval;

/// <summary>
/// Runs <see cref="AdventureSessionService"/> against a real PostgreSQL. The EF InMemory provider
/// can't show what these cover: the <c>AddAdventureSession</c> migration applying on top of the
/// full migration chain, the jsonb list columns round-tripping, in-place list mutations being
/// picked up by change tracking, and the foreign-key delete behaviours.
/// </summary>
public class AdventureSessionPostgresTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly PostgresFixture _fixture;
    private string _connectionString = null!;
    private AircaneDbContext _db = null!;
    private Campaign _campaign = null!;
    private GeneratedAdventure _adventure = null!;

    public AdventureSessionPostgresTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync();
        _db = PostgresFixture.CreateContext(_connectionString);

        _campaign = new Campaign("Postgres Campaign", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _adventure = BuildAdventure();
        _db.Campaigns.Add(_campaign);
        _db.GeneratedAdventures.Add(_adventure);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private AdventureSessionService CreateService(AircaneDbContext db) => new(
        db,
        new AdventureRetrievalService(db, NullLogger<AdventureRetrievalService>.Instance),
        new CampaignStateService(db, NullLogger<CampaignStateService>.Instance),
        NullLogger<AdventureSessionService>.Instance);

    /// <summary>A fresh context on the same database, so reads can't be served from the tracker.</summary>
    private AircaneDbContext NewContext() => PostgresFixture.CreateContext(_connectionString);

    private static GeneratedAdventure BuildAdventure()
    {
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "Library", Description = "d", SceneType = "exploration", ConnectsTo = ["scene-2"], Objectives = ["Find the tome"] },
            new() { SceneId = "scene-2", Title = "Hall", Description = "d", SceneType = "combat", ConnectsTo = [] },
        };
        var npcs = new List<GeneratedNpc>
        {
            new() { Name = "Guard Captain", Role = "villain", Personality = "p", StatsSummary = "s", SceneIds = ["scene-1"] },
            new() { Name = "Archivist", Role = "ally", Personality = "p", StatsSummary = "s", SceneIds = ["scene-1"] },
        };
        var clues = new AdventureClues
        {
            Secrets = [new AdventureSecret { Title = "Vault Key", Content = "c", SceneId = "scene-2", DiscoveryMethod = "d" }],
            Handouts = [new AdventureHandout { Title = "Torn letter", Content = "c", SceneId = "scene-1" }],
            FailForwardPaths = [],
        };

        var now = DateTime.UtcNow;
        return new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "Postgres Adventure",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = "{}",
            PitchJson = JsonSerializer.Serialize(new AdventurePitch { Title = "Postgres Adventure", Hook = "h", Summary = "s" }, CamelCase),
            ScenesJson = JsonSerializer.Serialize(scenes, CamelCase),
            NpcsJson = JsonSerializer.Serialize(npcs, CamelCase),
            CluesJson = JsonSerializer.Serialize(clues, CamelCase),
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [Fact]
    public async Task Start_PersistsNpcStatesAsJsonb_AndRoundTripsThroughAFreshContext()
    {
        var run = await CreateService(_db).StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);

        await using var fresh = NewContext();
        var saved = await fresh.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);

        Assert.Equal(AdventureSessionStatus.InProgress, saved.Status);
        Assert.Equal("scene-1", saved.CurrentSceneId);
        Assert.Equal(["npc-guard-captain", "npc-archivist"], saved.NpcStates.Select(n => n.NpcId));
        Assert.All(saved.NpcStates, n => Assert.True(n.IsAlive));
        Assert.Empty(saved.RevealedClueIds);
    }

    [Fact]
    public async Task InPlaceListChanges_AreDetectedAndPersisted()
    {
        var service = CreateService(_db);
        var run = await service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);

        // The service mutates the tracked lists in place (Add / property set on a list element);
        // without the snapshot comparer on the jsonb columns none of these would be saved.
        await service.RevealClueAsync(run.Id, "clue-1");
        await service.UpdateNpcStateAsync(run.Id, "npc-guard-captain", true, false, "knows-the-key", "Slain");
        await service.AdvanceSceneAsync(run.Id, "scene-2", "On to the hall.");
        await service.RevealSecretAsync(run.Id, "secret-1");
        await service.CompleteAdventureAsync(run.Id, "Victory.");

        await using var fresh = NewContext();
        var saved = await fresh.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);

        Assert.Equal(["clue-1"], saved.RevealedClueIds);
        Assert.Equal(["secret-1"], saved.RevealedSecretIds);
        Assert.Equal(["scene-1", "scene-2"], saved.CompletedSceneIds);
        var captain = saved.NpcStates.Single(n => n.NpcId == "npc-guard-captain");
        Assert.True(captain.HasBeenMet);
        Assert.False(captain.IsAlive);
        Assert.Equal(["knows-the-key"], captain.RevealedInfoIds);
        Assert.Equal("Slain", captain.PlayerNotes);
        Assert.Equal(AdventureSessionStatus.Completed, saved.Status);
        Assert.Equal("Victory.", saved.OutcomeNotes);
        Assert.NotNull(saved.CompletedAt);
    }

    [Fact]
    public async Task Transcript_RoundTripsInOrder_KeepsTheLast50_AndGoesWithTheRun()
    {
        var service = CreateService(_db);
        var run = await service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);
        for (var i = 1; i <= 30; i++)
            await service.RecordExchangeAsync(run.Id, $"action {i}", $"narration {i}", "scene-1");

        await using (var fresh = NewContext())
        {
            var history = await CreateService(fresh).GetHistoryAsync(run.Id);
            Assert.Equal(50, history.Count);
            Assert.Equal("action 6", history[0].Content);
            Assert.Equal("narration 30", history[^1].Content);
            Assert.Equal(history.Select(h => h.CreatedAt).Order(), history.Select(h => h.CreatedAt));
            Assert.All(history, h => Assert.Equal("scene-1", h.SceneId));
        }

        // Entries are tied to the run: deleting the adventure deletes the run and its transcript.
        await using (var db = NewContext())
        {
            db.GeneratedAdventures.Remove(await db.GeneratedAdventures.SingleAsync(a => a.Id == _adventure.Id));
            await db.SaveChangesAsync();
        }

        await using var after = NewContext();
        Assert.False(await after.AdventureSessionEntries.AnyAsync(e => e.AdventureSessionId == run.Id));
    }

    [Fact]
    public async Task PendingRoll_PersistsAndClears()
    {
        var service = CreateService(_db);
        var run = await service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);

        await service.UpdatePendingRollAsync(run.Id, "Dexterity (Stealth)", 14);
        await using (var fresh = NewContext())
        {
            var saved = await fresh.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);
            Assert.Equal("Dexterity (Stealth)", saved.PendingRollLabel);
            Assert.Equal(14, saved.PendingRollDC);
        }

        await service.UpdatePendingRollAsync(run.Id, null, null);
        await using (var fresh = NewContext())
        {
            var saved = await fresh.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == run.Id);
            Assert.Null(saved.PendingRollLabel);
            Assert.Null(saved.PendingRollDC);
        }
    }

    [Fact]
    public async Task ActiveRunLookup_FindsOnlyTheInProgressRun()
    {
        var service = CreateService(_db);
        var first = await service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);
        await service.AbandonAdventureAsync(first.Id);

        Assert.Null(await CreateService(NewContext()).GetActiveByCampaignAsync(_campaign.Id));

        var second = await service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);
        var active = await CreateService(NewContext()).GetActiveByCampaignAsync(_campaign.Id);

        Assert.Equal(second.Id, active!.Id);
    }

    [Fact]
    public async Task DeletingTheAdventure_CascadesToItsRuns()
    {
        var run = await CreateService(_db).StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);

        await using (var db = NewContext())
        {
            db.GeneratedAdventures.Remove(await db.GeneratedAdventures.SingleAsync(a => a.Id == _adventure.Id));
            await db.SaveChangesAsync();   // would throw a FK violation without ON DELETE CASCADE
        }

        await using var fresh = NewContext();
        Assert.False(await fresh.AdventureSessions.AnyAsync(s => s.Id == run.Id));
    }

    [Fact]
    public async Task DeletingTheCampaign_CascadesToItsRuns()
    {
        var run = await CreateService(_db).StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);

        await using (var db = NewContext())
        {
            db.Campaigns.Remove(await db.Campaigns.SingleAsync(c => c.Id == _campaign.Id));
            await db.SaveChangesAsync();
        }

        await using var fresh = NewContext();
        Assert.False(await fresh.AdventureSessions.AnyAsync(s => s.Id == run.Id));
    }

    [Fact]
    public async Task AuditEventsAreWrittenToTheCampaignLog()
    {
        var service = CreateService(_db);
        var run = await service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);
        await service.AdvanceSceneAsync(run.Id, "scene-2", null);

        await using var fresh = NewContext();
        var types = await fresh.CampaignEvents.AsNoTracking()
            .Where(e => e.CampaignId == _campaign.Id)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Equal(["Adventure:Started", "Adventure:SceneAdvanced"], types);
    }
}
