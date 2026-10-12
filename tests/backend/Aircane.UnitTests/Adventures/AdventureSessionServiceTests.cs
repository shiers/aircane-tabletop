using Aircane.Application.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Aircane.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>
/// Unit tests for <see cref="AdventureSessionService"/> against the in-memory provider, using the
/// real retrieval and campaign-state services so JSON round-tripping and audit events are exercised.
/// </summary>
public sealed class AdventureSessionServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly RecordingSessionHubNotifier _hub = new();
    private readonly AdventureSessionService _sut;

    private Campaign _campaign = null!;
    private GeneratedAdventure _adventure = null!;

    public AdventureSessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _sut = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance,
            _hub);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SeedAsync(GeneratedAdventureStatus status = GeneratedAdventureStatus.Approved)
    {
        _campaign = new Campaign("Test Campaign", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _adventure = AdventureSessionTestData.CreateAdventure(status);
        _db.Campaigns.Add(_campaign);
        _db.GeneratedAdventures.Add(_adventure);
        await _db.SaveChangesAsync();
    }

    private async Task<AdventureSession> StartAsync(Guid? playSessionId = null)
    {
        await SeedAsync();
        var session = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);
        if (playSessionId is not null)
            await _sut.AttachPlaySessionAsync(session.Id, playSessionId.Value);
        return session;
    }

    private Task<AdventureSession> ReloadAsync(Guid id) =>
        _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == id);

    private async Task<Aircane.Application.DTOs.Adventures.GeneratedAdventureDto> LoadDtoAsync()
    {
        var retrieval = new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance);
        return (await retrieval.GetByIdAsync(_adventure.Id))!;
    }

    // ── StartSession ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_StartSession_InitializesNpcStatesForAllNpcs()
    {
        var session = await StartAsync();

        var saved = await ReloadAsync(session.Id);
        Assert.Equal(2, saved.NpcStates.Count);
        Assert.Contains(saved.NpcStates, n => n.NpcId == "npc-guard-captain" && n.NpcName == "Guard Captain");
        Assert.Contains(saved.NpcStates, n => n.NpcId == "npc-archivist" && n.NpcName == "Archivist");
        Assert.All(saved.NpcStates, n =>
        {
            Assert.False(n.HasBeenMet);
            Assert.True(n.IsAlive);
            Assert.Empty(n.RevealedInfoIds);
        });
        Assert.Empty(saved.RevealedClueIds);
        Assert.Empty(saved.RevealedSecretIds);
        Assert.Empty(saved.CompletedSceneIds);
    }

    [Fact]
    public async Task AdventureSessionService_StartSession_SetsFirstSceneAsCurrent()
    {
        var session = await StartAsync();

        var saved = await ReloadAsync(session.Id);
        Assert.Equal("scene-1", saved.CurrentSceneId);
        Assert.Equal(AdventureSessionStatus.InProgress, saved.Status);
        Assert.Equal(_campaign.Id, saved.CampaignId);
        Assert.Equal(_adventure.Id, saved.GeneratedAdventureId);
        Assert.Null(saved.CompletedAt);
    }

    [Fact]
    public async Task AdventureSessionService_StartSession_WhenAlreadyActive_ReturnsExisting()
    {
        var first = await StartAsync();

        var second = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.AdventureSessions.CountAsync());
    }

    [Fact]
    public async Task AdventureSessionService_StartSession_DifferentAdventureInProgress_Throws()
    {
        await StartAsync();
        var other = AdventureSessionTestData.CreateAdventure();
        _db.GeneratedAdventures.Add(other);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartAdventureSessionAsync(other.Id, _campaign.Id, characterId: null));
    }

    [Fact]
    public async Task AdventureSessionService_StartSession_DraftAdventure_Throws()
    {
        await SeedAsync(GeneratedAdventureStatus.Draft);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null));
    }

    [Fact]
    public async Task AdventureSessionService_StartSession_UnknownAdventure_ThrowsNotFound()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.StartAdventureSessionAsync(Guid.NewGuid(), _campaign.Id, characterId: null));
    }

    [Fact]
    public async Task AdventureSessionService_StartSession_UnknownCampaign_ThrowsNotFound()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.StartAdventureSessionAsync(_adventure.Id, Guid.NewGuid(), characterId: null));
    }

    // ── AdvanceScene ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_AdvanceScene_MovesCurrentSceneToCompleted()
    {
        var session = await StartAsync();

        await _sut.AdvanceSceneAsync(session.Id, "scene-2", "You wade into the hall.");

        var saved = await ReloadAsync(session.Id);
        Assert.Equal("scene-2", saved.CurrentSceneId);
        Assert.Equal(["scene-1"], saved.CompletedSceneIds);
    }

    [Fact]
    public async Task AdventureSessionService_AdvanceScene_InvalidSceneId_Throws()
    {
        var session = await StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.AdvanceSceneAsync(session.Id, "scene-99", null));

        var saved = await ReloadAsync(session.Id);
        Assert.Equal("scene-1", saved.CurrentSceneId);
        Assert.Empty(saved.CompletedSceneIds);
    }

    [Fact]
    public async Task AdventureSessionService_AdvanceScene_SceneNotReachableFromCurrent_Throws()
    {
        var session = await StartAsync();
        await _sut.AdvanceSceneAsync(session.Id, "scene-2", null);

        // scene-2 only connects to scene-3, so going back to scene-1 is not an exit.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.AdvanceSceneAsync(session.Id, "scene-1", null));
    }

    [Fact]
    public async Task AdventureSessionService_AdvanceScene_AcceptsSceneIdInAnyCase()
    {
        var session = await StartAsync();

        await _sut.AdvanceSceneAsync(session.Id, "Scene-2", null);

        Assert.Equal("scene-2", (await ReloadAsync(session.Id)).CurrentSceneId);
    }

    [Fact]
    public async Task AdventureSessionService_AdvanceScene_BroadcastsToPlaySessionAndAppendsAuditEvent()
    {
        var playSessionId = Guid.NewGuid();
        var session = await StartAsync(playSessionId);

        await _sut.AdvanceSceneAsync(session.Id, "scene-2", "Into the hall.");

        var (group, evt) = Assert.Single(_hub.SceneAdvanced);
        Assert.Equal(playSessionId, group);
        Assert.Equal(session.Id, evt.SessionId);
        Assert.Equal("scene-1", evt.PreviousSceneId);
        Assert.Equal("scene-2", evt.NewSceneId);
        Assert.Equal("Into the hall.", evt.Narration);

        var audit = await _db.CampaignEvents.Where(e => e.EventType == "Adventure:SceneAdvanced").ToListAsync();
        var logged = Assert.Single(audit);
        Assert.False(logged.Reversible);
        Assert.Equal(_campaign.Id, logged.CampaignId);
    }

    [Fact]
    public async Task AdventureSessionService_AdvanceScene_WithoutPlaySession_StillAdvancesAndDoesNotBroadcast()
    {
        var session = await StartAsync();

        await _sut.AdvanceSceneAsync(session.Id, "scene-2", null);

        Assert.Empty(_hub.SceneAdvanced);
        Assert.Equal("scene-2", (await ReloadAsync(session.Id)).CurrentSceneId);
    }

    // ── Reveal clues / secrets ────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_RevealClue_AddsToRevealedList()
    {
        var playSessionId = Guid.NewGuid();
        var session = await StartAsync(playSessionId);

        await _sut.RevealClueAsync(session.Id, "clue-1");

        Assert.Equal(["clue-1"], (await ReloadAsync(session.Id)).RevealedClueIds);
        var (group, evt) = Assert.Single(_hub.CluesRevealed);
        Assert.Equal(playSessionId, group);
        Assert.Equal("The torn letter", evt.ClueName);
        Assert.Equal("Meet me below the east stair.", evt.Description);
    }

    [Fact]
    public async Task AdventureSessionService_RevealClue_Idempotent_DoesNotDuplicate()
    {
        var session = await StartAsync(Guid.NewGuid());

        await _sut.RevealClueAsync(session.Id, "clue-1");
        await _sut.RevealClueAsync(session.Id, "clue-1");

        Assert.Single((await ReloadAsync(session.Id)).RevealedClueIds);
        Assert.Single(_hub.CluesRevealed);
    }

    [Fact]
    public async Task AdventureSessionService_RevealClue_UnknownClue_Throws()
    {
        var session = await StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RevealClueAsync(session.Id, "clue-42"));
    }

    [Fact]
    public async Task AdventureSessionService_RevealSecret_AddsToRevealedListAndIsIdempotent()
    {
        var session = await StartAsync(Guid.NewGuid());

        await _sut.RevealSecretAsync(session.Id, "secret-1");
        await _sut.RevealSecretAsync(session.Id, "secret-1");

        Assert.Equal(["secret-1"], (await ReloadAsync(session.Id)).RevealedSecretIds);
        var (_, evt) = Assert.Single(_hub.SecretsRevealed);
        Assert.Equal("The Vault Key", evt.SecretName);
    }

    // ── UpdateNpc ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_UpdateNpc_PatchesOnlyNonNullFields()
    {
        var session = await StartAsync(Guid.NewGuid());

        await _sut.UpdateNpcStateAsync(session.Id, "npc-guard-captain",
            hasBeenMet: true, isAlive: null, revealedInfoId: null, playerNotes: "Suspicious of strangers");
        await _sut.UpdateNpcStateAsync(session.Id, "npc-guard-captain",
            hasBeenMet: null, isAlive: false, revealedInfoId: "knows-the-key", playerNotes: null);

        var npc = (await ReloadAsync(session.Id)).NpcStates.Single(n => n.NpcId == "npc-guard-captain");
        Assert.True(npc.HasBeenMet);                                  // from call 1, untouched by call 2
        Assert.False(npc.IsAlive);                                    // from call 2
        Assert.Equal("Suspicious of strangers", npc.PlayerNotes);     // from call 1, untouched by call 2
        Assert.Equal(["knows-the-key"], npc.RevealedInfoIds);

        // The other NPC is unaffected.
        var archivist = (await ReloadAsync(session.Id)).NpcStates.Single(n => n.NpcId == "npc-archivist");
        Assert.False(archivist.HasBeenMet);
        Assert.True(archivist.IsAlive);

        Assert.Equal(2, _hub.NpcsUpdated.Count);
        Assert.False(_hub.NpcsUpdated[1].Event.IsAlive);
        Assert.True(_hub.NpcsUpdated[1].Event.HasBeenMet);
    }

    [Fact]
    public async Task AdventureSessionService_UpdateNpc_NoChange_DoesNotBroadcast()
    {
        var session = await StartAsync(Guid.NewGuid());

        await _sut.UpdateNpcStateAsync(session.Id, "npc-archivist", null, null, null, null);
        await _sut.UpdateNpcStateAsync(session.Id, "npc-archivist", hasBeenMet: false, isAlive: true, null, null);

        Assert.Empty(_hub.NpcsUpdated);
    }

    [Fact]
    public async Task AdventureSessionService_UpdateNpc_UnknownNpc_Throws()
    {
        var session = await StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateNpcStateAsync(session.Id, "npc-nobody", true, null, null, null));
    }

    // ── Complete / abandon ────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_CompleteAdventure_SetsStatusAndTimestamp()
    {
        var session = await StartAsync(Guid.NewGuid());
        var before = DateTimeOffset.UtcNow;

        await _sut.CompleteAdventureAsync(session.Id, "The tome was recovered.");

        var saved = await ReloadAsync(session.Id);
        Assert.Equal(AdventureSessionStatus.Completed, saved.Status);
        Assert.Equal("The tome was recovered.", saved.OutcomeNotes);
        Assert.NotNull(saved.CompletedAt);
        Assert.True(saved.CompletedAt >= before);
        Assert.Contains("scene-1", saved.CompletedSceneIds);
        var (_, evt) = Assert.Single(_hub.Completed);
        Assert.Equal("The tome was recovered.", evt.OutcomeNotes);
    }

    [Fact]
    public async Task AdventureSessionService_CompleteAdventure_ThenChange_Throws()
    {
        var session = await StartAsync();
        await _sut.CompleteAdventureAsync(session.Id, "Done.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AdvanceSceneAsync(session.Id, "scene-2", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RevealClueAsync(session.Id, "clue-1"));
    }

    [Fact]
    public async Task AdventureSessionService_AbandonAdventure_SetsStatusAndAllowsNewRun()
    {
        var session = await StartAsync();

        await _sut.AbandonAdventureAsync(session.Id);
        await _sut.AbandonAdventureAsync(session.Id); // idempotent

        var saved = await ReloadAsync(session.Id);
        Assert.Equal(AdventureSessionStatus.Abandoned, saved.Status);
        Assert.NotNull(saved.CompletedAt);
        Assert.Null(await _sut.GetActiveByCampaignAsync(_campaign.Id));

        var fresh = await _sut.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, characterId: null);
        Assert.NotEqual(session.Id, fresh.Id);
    }

    // ── GetSessionState ───────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_GetSessionState_ReturnsSceneExitsNpcsAndCounts()
    {
        var session = await StartAsync();
        await _sut.RevealClueAsync(session.Id, "clue-1");

        var state = await _sut.GetSessionStateAsync(session.Id);

        Assert.Equal("The Sunken Archive", state.AdventureTitle);
        Assert.Equal("InProgress", state.Status);
        Assert.Equal("scene-1", state.CurrentScene.SceneId);
        Assert.Equal(1, state.CurrentSceneNumber);
        Assert.Equal(3, state.TotalScenes);
        Assert.Equal(0, state.CompletedSceneCount);
        Assert.Equal(["Find the missing tome", "Deal with the guard"], state.CurrentScene.Objectives);
        Assert.Equal(["scene-2", "scene-3"], state.CurrentScene.Exits.Select(e => e.SceneId));
        Assert.Equal(["The Grand Hall", "The Hidden Passage"], state.CurrentScene.Exits.Select(e => e.Title));
        Assert.Equal(["Guard Captain", "Archivist"], state.CurrentScene.Npcs.Select(n => n.NpcName));
        Assert.Equal(2, state.NpcStates.Count);
        var clue = Assert.Single(state.RevealedClues);
        Assert.Equal("The torn letter", clue.Name);
        Assert.Empty(state.RevealedSecrets);
    }

    [Fact]
    public async Task AdventureSessionService_GetSessionState_DoesNotLeakDmOnlyContent()
    {
        var session = await StartAsync();

        var state = await _sut.GetSessionStateAsync(session.Id);
        var json = System.Text.Json.JsonSerializer.Serialize(state);

        Assert.DoesNotContain("The tome is behind the third shelf", json);   // DM notes
        Assert.DoesNotContain("The key is in the fountain", json);           // undiscovered secret
    }

    [Fact]
    public async Task AdventureSessionService_GetAvailableScenes_ReturnsOnlyReachableScenes()
    {
        var session = await StartAsync();
        await _sut.AdvanceSceneAsync(session.Id, "scene-2", null);

        var scenes = await _sut.GetAvailableScenesAsync(session.Id);

        var only = Assert.Single(scenes);
        Assert.Equal("scene-3", only.SceneId);
    }

    [Fact]
    public async Task AdventureSessionService_GetSessionState_UnknownSession_ThrowsNotFound()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.GetSessionStateAsync(Guid.NewGuid()));
    }

    // ── BuildAdventureContext ─────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionService_BuildAdventureContext_IncludesCurrentSceneAndObjectives()
    {
        var session = await StartAsync();

        var context = _sut.BuildAdventureContext(await ReloadAsync(session.Id), await LoadDtoAsync());

        Assert.Contains("Current Adventure: The Sunken Archive", context);
        Assert.Contains("Current Scene: The Sunken Library [scene-1]", context);
        Assert.Contains("Flooded shelves rise out of black water.", context);
        Assert.Contains("Scene Objectives:", context);
        Assert.Contains("- Find the missing tome", context);
        Assert.Contains("- Deal with the guard", context);
        Assert.Contains("Available Exits:", context);
        Assert.Contains("- The Grand Hall [scene-2]", context);
        Assert.Contains("- The Hidden Passage [scene-3]", context);
        Assert.Contains("NPCs Present:", context);
        Assert.Contains("Guard Captain [npc-guard-captain]", context);
        Assert.Contains("alive, not yet met", context);
        Assert.Contains("Completed Scenes: 0 of 3", context);
    }

    [Fact]
    public async Task AdventureSessionService_BuildAdventureContext_ListsWhatIsDiscoverableWithIds()
    {
        var session = await StartAsync();

        var context = _sut.BuildAdventureContext(await ReloadAsync(session.Id), await LoadDtoAsync());

        // The AI can only emit revealClue / advanceScene with IDs it has been shown.
        Assert.Contains("The torn letter [clue-1]", context);
        Assert.Contains("If The Player Gets Stuck:", context);
        // scene-1 has no secret; the scene-2 secret must not be in scene-1's context.
        Assert.DoesNotContain("The Vault Key", context);
    }

    [Fact]
    public async Task AdventureSessionService_BuildAdventureContext_IncludesRevealedClues()
    {
        var session = await StartAsync();
        await _sut.RevealClueAsync(session.Id, "clue-1");
        await _sut.AdvanceSceneAsync(session.Id, "scene-2", null);
        await _sut.RevealSecretAsync(session.Id, "secret-1");

        var context = _sut.BuildAdventureContext(await ReloadAsync(session.Id), await LoadDtoAsync());

        Assert.Contains("Already Revealed:", context);
        Assert.Contains("Clues: The torn letter", context);
        Assert.Contains("Secrets: The Vault Key", context);
        Assert.Contains("Completed Scenes: 1 of 3", context);
        // Once revealed, the clue is no longer listed as findable.
        Assert.DoesNotContain("Clues Findable Here", context);
    }

    [Fact]
    public async Task AdventureSessionService_BuildAdventureContext_ShowsDeadAndMetNpcState()
    {
        var session = await StartAsync();
        await _sut.UpdateNpcStateAsync(session.Id, "npc-guard-captain", true, false, null, "Slain at the door");

        var context = _sut.BuildAdventureContext(await ReloadAsync(session.Id), await LoadDtoAsync());

        Assert.Contains("dead, already met", context);
        Assert.Contains("Notes: Slain at the door", context);
    }

    [Fact]
    public async Task AdventureSessionService_BuildActiveAdventureContext_NoActiveRun_ReturnsNull()
    {
        await SeedAsync();

        Assert.Null(await _sut.BuildActiveAdventureContextAsync(_campaign.Id));
    }

    [Fact]
    public async Task AdventureSessionService_BuildActiveAdventureContext_ReturnsContextForActiveRun()
    {
        var session = await StartAsync();

        var context = await _sut.BuildActiveAdventureContextAsync(_campaign.Id);

        Assert.NotNull(context);
        Assert.Equal(session.Id, context!.SessionId);
        Assert.Contains("Current Scene: The Sunken Library", context.Text);
    }

    // ── Content IDs ───────────────────────────────────────────────────────────

    [Fact]
    public void AdventureContentIds_ForNpcs_DisambiguatesDuplicateNames()
    {
        var npcs = new[]
        {
            new Aircane.Application.DTOs.Adventures.GeneratedNpc { Name = "Old Guard", Role = "x", Personality = "x", StatsSummary = "x", SceneIds = [] },
            new Aircane.Application.DTOs.Adventures.GeneratedNpc { Name = "old  guard!", Role = "x", Personality = "x", StatsSummary = "x", SceneIds = [] },
            new Aircane.Application.DTOs.Adventures.GeneratedNpc { Name = "???", Role = "x", Personality = "x", StatsSummary = "x", SceneIds = [] },
        };

        var ids = AdventureContentIds.ForNpcs(npcs).Select(n => n.Id).ToList();

        Assert.Equal(["npc-old-guard", "npc-old-guard-2", "npc-unnamed"], ids);
    }
}
