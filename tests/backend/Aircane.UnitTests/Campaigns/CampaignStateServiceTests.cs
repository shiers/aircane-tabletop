using System.Text.Json;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Campaigns;

/// <summary>
/// Unit tests for <see cref="CampaignStateService"/> using an in-memory EF Core database.
/// </summary>
public class CampaignStateServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly CampaignStateService _sut;

    public CampaignStateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _sut = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Campaign> SeedCampaignAsync()
    {
        var campaign = new Campaign("Test Campaign", "D&D 5e", "2014");
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();
        return campaign;
    }

    // ── LoadStateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadStateAsync_NoSnapshot_ReturnsDefaultState()
    {
        var campaign = await SeedCampaignAsync();

        var state = await _sut.LoadStateAsync(campaign.Id);

        Assert.Equal(campaign.Id, state.CampaignId);
        Assert.Null(state.ActiveSessionId);
        Assert.Null(state.CurrentSceneJson);

        var parsed = JsonSerializer.Deserialize<JsonElement>(state.StateJson);
        Assert.True(parsed.TryGetProperty("currentSceneId", out _));
        Assert.True(parsed.TryGetProperty("partyResources", out _));
        Assert.True(parsed.TryGetProperty("worldFlags", out _));
        Assert.True(parsed.TryGetProperty("revealedContentIds", out _));
        Assert.True(parsed.TryGetProperty("npcStates", out _));
        Assert.True(parsed.TryGetProperty("activeEncounterId", out _));
    }

    [Fact]
    public async Task LoadStateAsync_WithSnapshot_ReturnsStoredState()
    {
        var campaign = await SeedCampaignAsync();
        var snapshot = new GameStateSnapshot(
            campaignId: campaign.Id,
            stateJson: """{"currentSceneId":"scene-1","partyResources":{"gold":100}}""",
            currentSceneId: "scene-1");
        _db.GameStateSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        var state = await _sut.LoadStateAsync(campaign.Id);

        Assert.Equal(campaign.Id, state.CampaignId);
        Assert.Equal("scene-1", state.CurrentSceneJson);
        Assert.Contains("gold", state.StateJson);
    }

    [Fact]
    public async Task LoadStateAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.LoadStateAsync(Guid.NewGuid()));
    }

    // ── ApplyCommandAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyCommandAsync_MoveScene_UpdatesCurrentSceneId()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "MoveScene",
            PayloadJson: """{"sceneId":"tavern-entrance"}""",
            ActorType: "Host");

        var result = await _sut.ApplyCommandAsync(request);

        Assert.Equal("tavern-entrance", result.CurrentSceneJson);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        Assert.Equal("tavern-entrance", parsed.GetProperty("currentSceneId").GetString());
    }

    [Fact]
    public async Task ApplyCommandAsync_SetWorldFlag_AddsFlag()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "SetWorldFlag",
            PayloadJson: """{"key":"dragon_defeated","value":true}""",
            ActorType: "AI");

        var result = await _sut.ApplyCommandAsync(request);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        var flags = parsed.GetProperty("worldFlags");
        Assert.True(flags.GetProperty("dragon_defeated").GetBoolean());
    }

    [Fact]
    public async Task ApplyCommandAsync_RevealContent_AddsContentId()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "RevealContent",
            PayloadJson: """{"contentId":"secret-door-map"}""",
            ActorType: "Host");

        var result = await _sut.ApplyCommandAsync(request);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        var revealed = parsed.GetProperty("revealedContentIds");
        Assert.Equal(1, revealed.GetArrayLength());
        Assert.Equal("secret-door-map", revealed[0].GetString());
    }

    [Fact]
    public async Task ApplyCommandAsync_RevealContent_DoesNotDuplicate()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "RevealContent",
            PayloadJson: """{"contentId":"map-1"}""",
            ActorType: "Host");

        await _sut.ApplyCommandAsync(request);
        var result = await _sut.ApplyCommandAsync(request);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        var revealed = parsed.GetProperty("revealedContentIds");
        Assert.Equal(1, revealed.GetArrayLength());
    }

    [Fact]
    public async Task ApplyCommandAsync_UpdatePartyResources_OverwritesResources()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "UpdatePartyResources",
            PayloadJson: """{"resources":{"gold":250,"potions":3}}""",
            ActorType: "Host");

        var result = await _sut.ApplyCommandAsync(request);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        var resources = parsed.GetProperty("partyResources");
        Assert.Equal(250, resources.GetProperty("gold").GetInt32());
        Assert.Equal(3, resources.GetProperty("potions").GetInt32());
    }

    [Fact]
    public async Task ApplyCommandAsync_GenericCommand_MergesPayloadIntoState()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "CustomAction",
            PayloadJson: """{"activeEncounterId":"enc-42"}""",
            ActorType: "AI");

        var result = await _sut.ApplyCommandAsync(request);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        Assert.Equal("enc-42", parsed.GetProperty("activeEncounterId").GetString());
    }

    [Fact]
    public async Task ApplyCommandAsync_AppendsReversibleEvent()
    {
        var campaign = await SeedCampaignAsync();

        var request = new ApplyCommandRequest(
            CampaignId: campaign.Id,
            CommandType: "MoveScene",
            PayloadJson: """{"sceneId":"forest"}""",
            ActorType: "Host");

        await _sut.ApplyCommandAsync(request);

        var events = await _db.CampaignEvents
            .Where(e => e.CampaignId == campaign.Id)
            .ToListAsync();

        Assert.Single(events);
        Assert.Equal("Command:MoveScene", events[0].EventType);
        Assert.True(events[0].Reversible);
        Assert.Equal("Host", events[0].ActorType);
    }

    [Fact]
    public async Task ApplyCommandAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        var request = new ApplyCommandRequest(
            CampaignId: Guid.NewGuid(),
            CommandType: "MoveScene",
            PayloadJson: """{"sceneId":"x"}""",
            ActorType: "Host");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.ApplyCommandAsync(request));
    }

    // ── AppendEventAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AppendEventAsync_ValidRequest_CreatesEvent()
    {
        var campaign = await SeedCampaignAsync();

        var request = new AppendEventRequest(
            CampaignId: campaign.Id,
            EventType: "PlayerAction",
            PayloadJson: """{"action":"attack","target":"goblin"}""",
            ActorType: "Player",
            ActorId: Guid.NewGuid());

        var result = await _sut.AppendEventAsync(request);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(campaign.Id, result.CampaignId);
        Assert.Equal("PlayerAction", result.EventType);
        Assert.Equal("Player", result.ActorType);
    }

    [Fact]
    public async Task AppendEventAsync_DoesNotModifySnapshot()
    {
        var campaign = await SeedCampaignAsync();

        // Apply a command first to create a snapshot
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"cave"}""", "Host"));

        // Append a raw event
        await _sut.AppendEventAsync(new AppendEventRequest(
            campaign.Id, "NarrativeNote", """{"text":"The wind howls"}""", "AI"));

        // Snapshot should still show the cave scene
        var state = await _sut.LoadStateAsync(campaign.Id);
        Assert.Equal("cave", state.CurrentSceneJson);
    }

    [Fact]
    public async Task AppendEventAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        var request = new AppendEventRequest(
            CampaignId: Guid.NewGuid(),
            EventType: "Test",
            PayloadJson: "{}",
            ActorType: "System");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.AppendEventAsync(request));
    }

    // ── UndoLastCommandAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UndoLastCommandAsync_RestoresPreviousState()
    {
        var campaign = await SeedCampaignAsync();

        // Apply two commands
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"village"}""", "Host"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"dungeon"}""", "Host"));

        // Undo the last one
        var result = await _sut.UndoLastCommandAsync(campaign.Id);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result.StateJson);
        Assert.Equal("village", parsed.GetProperty("currentSceneId").GetString());
    }

    [Fact]
    public async Task UndoLastCommandAsync_NoReversibleCommands_ThrowsInvalidOperation()
    {
        var campaign = await SeedCampaignAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UndoLastCommandAsync(campaign.Id));
    }

    [Fact]
    public async Task UndoLastCommandAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UndoLastCommandAsync(Guid.NewGuid()));
    }

    // ── GetEventLogAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventLogAsync_ReturnsEventsOrderedByMostRecentFirst()
    {
        var campaign = await SeedCampaignAsync();

        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"a"}""", "Host"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"b"}""", "Host"));

        var events = await _sut.GetEventLogAsync(campaign.Id);

        Assert.Equal(2, events.Count);
        Assert.True(events[0].CreatedAt >= events[1].CreatedAt);
    }

    [Fact]
    public async Task GetEventLogAsync_SupportsPagination()
    {
        var campaign = await SeedCampaignAsync();

        for (int i = 0; i < 5; i++)
        {
            await _sut.ApplyCommandAsync(new ApplyCommandRequest(
                campaign.Id, "MoveScene", $$$"""{"sceneId":"scene-{{{i}}}"}""", "Host"));
        }

        var page1 = await _sut.GetEventLogAsync(campaign.Id, page: 1, pageSize: 2);
        var page2 = await _sut.GetEventLogAsync(campaign.Id, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.NotEqual(page1[0].Id, page2[0].Id);
    }

    [Fact]
    public async Task GetEventLogAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.GetEventLogAsync(Guid.NewGuid()));
    }

    // ── Integration: full workflow ────────────────────────────────────────────

    [Fact]
    public async Task FullWorkflow_LoadApplyUndoLoad_MaintainsConsistency()
    {
        var campaign = await SeedCampaignAsync();

        // Initial state is empty
        var initial = await _sut.LoadStateAsync(campaign.Id);
        Assert.Null(initial.CurrentSceneJson);

        // Apply a scene change
        var afterMove = await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"market"}""", "Host"));
        Assert.Equal("market", afterMove.CurrentSceneJson);

        // Set a world flag
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "SetWorldFlag", """{"key":"quest_accepted","value":true}""", "Player"));

        // Load state — should reflect both changes
        var loaded = await _sut.LoadStateAsync(campaign.Id);
        var parsed = JsonSerializer.Deserialize<JsonElement>(loaded.StateJson);
        Assert.Equal("market", parsed.GetProperty("currentSceneId").GetString());
        Assert.True(parsed.GetProperty("worldFlags").GetProperty("quest_accepted").GetBoolean());

        // Undo the last command (SetWorldFlag)
        var afterUndo = await _sut.UndoLastCommandAsync(campaign.Id);
        var undoParsed = JsonSerializer.Deserialize<JsonElement>(afterUndo.StateJson);
        Assert.Equal("market", undoParsed.GetProperty("currentSceneId").GetString());
        // World flag should be gone (restored to state before SetWorldFlag)
    }

    // ── ReplayEventsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayEventsAsync_NoEvents_ReturnsDefaultState()
    {
        var campaign = await SeedCampaignAsync();

        var replayed = await _sut.ReplayEventsAsync(campaign.Id);

        Assert.Equal(campaign.Id, replayed.CampaignId);
        var parsed = JsonSerializer.Deserialize<JsonElement>(replayed.StateJson);
        Assert.True(parsed.TryGetProperty("currentSceneId", out var scene));
        Assert.Equal(JsonValueKind.Null, scene.ValueKind);
    }

    [Fact]
    public async Task ReplayEventsAsync_SingleCommand_RebuildsState()
    {
        var campaign = await SeedCampaignAsync();

        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"throne-room"}""", "Host"));

        var replayed = await _sut.ReplayEventsAsync(campaign.Id);

        var parsed = JsonSerializer.Deserialize<JsonElement>(replayed.StateJson);
        Assert.Equal("throne-room", parsed.GetProperty("currentSceneId").GetString());
    }

    [Fact]
    public async Task ReplayEventsAsync_MultipleCommands_RebuildsFullState()
    {
        var campaign = await SeedCampaignAsync();

        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"forest"}""", "Host"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "SetWorldFlag", """{"key":"bridge_repaired","value":true}""", "Player"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "RevealContent", """{"contentId":"hidden-passage"}""", "AI"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "UpdatePartyResources", """{"resources":{"gold":500,"rations":10}}""", "Host"));

        var replayed = await _sut.ReplayEventsAsync(campaign.Id);

        var parsed = JsonSerializer.Deserialize<JsonElement>(replayed.StateJson);
        Assert.Equal("forest", parsed.GetProperty("currentSceneId").GetString());
        Assert.True(parsed.GetProperty("worldFlags").GetProperty("bridge_repaired").GetBoolean());
        Assert.Equal("hidden-passage", parsed.GetProperty("revealedContentIds")[0].GetString());
        Assert.Equal(500, parsed.GetProperty("partyResources").GetProperty("gold").GetInt32());
        Assert.Equal(10, parsed.GetProperty("partyResources").GetProperty("rations").GetInt32());
    }

    [Fact]
    public async Task ReplayEventsAsync_MatchesCurrentSnapshot()
    {
        var campaign = await SeedCampaignAsync();

        // Apply several commands
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"cave"}""", "Host"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "SetWorldFlag", """{"key":"torch_lit","value":true}""", "Player"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "RevealContent", """{"contentId":"treasure-map"}""", "Host"));

        // Load the current snapshot
        var snapshot = await _sut.LoadStateAsync(campaign.Id);

        // Replay from events
        var replayed = await _sut.ReplayEventsAsync(campaign.Id);

        // Both should produce the same state
        var snapshotState = JsonSerializer.Deserialize<JsonElement>(snapshot.StateJson);
        var replayedState = JsonSerializer.Deserialize<JsonElement>(replayed.StateJson);

        Assert.Equal(
            snapshotState.GetProperty("currentSceneId").GetString(),
            replayedState.GetProperty("currentSceneId").GetString());
        Assert.Equal(
            snapshotState.GetProperty("worldFlags").GetProperty("torch_lit").GetBoolean(),
            replayedState.GetProperty("worldFlags").GetProperty("torch_lit").GetBoolean());
        Assert.Equal(
            snapshotState.GetProperty("revealedContentIds")[0].GetString(),
            replayedState.GetProperty("revealedContentIds")[0].GetString());
    }

    [Fact]
    public async Task ReplayEventsAsync_IgnoresNonCommandEvents()
    {
        var campaign = await SeedCampaignAsync();

        // Apply a command
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"tavern"}""", "Host"));

        // Append a non-command event (should be ignored during replay)
        await _sut.AppendEventAsync(new AppendEventRequest(
            campaign.Id, "NarrativeNote", """{"text":"A bard plays a tune"}""", "AI"));

        var replayed = await _sut.ReplayEventsAsync(campaign.Id);

        var parsed = JsonSerializer.Deserialize<JsonElement>(replayed.StateJson);
        Assert.Equal("tavern", parsed.GetProperty("currentSceneId").GetString());
    }

    [Fact]
    public async Task ReplayEventsAsync_UnknownCampaign_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.ReplayEventsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReplayEventsAsync_AfterUndo_ExcludesRemovedEvent()
    {
        var campaign = await SeedCampaignAsync();

        // Apply two commands
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"village"}""", "Host"));
        await _sut.ApplyCommandAsync(new ApplyCommandRequest(
            campaign.Id, "MoveScene", """{"sceneId":"dungeon"}""", "Host"));

        // Undo the last command (removes it from the event log)
        await _sut.UndoLastCommandAsync(campaign.Id);

        // Replay should only reflect the first command
        var replayed = await _sut.ReplayEventsAsync(campaign.Id);

        var parsed = JsonSerializer.Deserialize<JsonElement>(replayed.StateJson);
        Assert.Equal("village", parsed.GetProperty("currentSceneId").GetString());
    }
}
