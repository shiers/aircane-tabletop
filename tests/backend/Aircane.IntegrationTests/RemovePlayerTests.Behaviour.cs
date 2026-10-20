using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Sessions;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

public sealed partial class RemovePlayerTests
{
    private async Task SetPendingRollAsync(Game game, string label)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdventureSessionService>().UpdatePendingRollAsync(game.RunId, label, 14);
    }

    private async Task<HttpResponseMessage> AdvanceAsync(Game game)
    {
        using var host = Host();
        return await host.PostAsync($"/api/adventure-sessions/{game.RunId}/advance-turn", null);
    }

    private async Task<HttpResponseMessage> AddToRunAsync(Game game, Guid participantId)
    {
        using var host = Host();
        return await host.PostAsJsonAsync($"/api/adventure-sessions/{game.RunId}/participants", new { participantId });
    }

    private IReadOnlyList<HubRecorder.Call> Events(Table table, string method) => _factory.Hub.For(table.Session.Id, method);

    // ═════════════════════════════════════════════════════════════════════════
    // 2. The turn order
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemovingTheActingPlayer_PassesTheTurnOn_ClearsTheRoll_AndLeavesNoDanglingId()
    {
        var game = await StartGameAsync("Alice", "Bob", "Cara");
        await SetPendingRollAsync(game, "Dexterity (Stealth)");

        using var response = await RemoveAsync(game.Table, game[0].ParticipantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RemoveParticipantResult>(Web))!;
        Assert.Equal([game.RunId], result.TurnPassedInRunIds);
        var run = await RunAsync(game);
        Assert.Equal([game[1].ParticipantId, game[2].ParticipantId], run.TurnOrder);
        Assert.Equal(game[1].ParticipantId, run.ActiveParticipantId);
        Assert.Equal(1, run.CurrentRound);   // Alice to Bob does not wrap
        Assert.Null(run.PendingRollLabel);
        Assert.Null(run.PendingRollDC);

        // Bob can act straight away.
        using var bob = As(game[1].Token);
        using var acted = await ActAsync(bob, game);
        Assert.Equal(HttpStatusCode.OK, acted.StatusCode);
    }

    [Fact]
    public async Task RemovingTheLastInOrder_WhileTheyAct_WrapsToTheFirst_AndStartsANewRound()
    {
        var game = await StartGameAsync("Alice", "Bob", "Cara");
        (await AdvanceAsync(game)).EnsureSuccessStatusCode();   // Bob
        (await AdvanceAsync(game)).EnsureSuccessStatusCode();   // Cara

        (await RemoveAsync(game.Table, game[2].ParticipantId)).EnsureSuccessStatusCode();

        var run = await RunAsync(game);
        Assert.Equal(game[0].ParticipantId, run.ActiveParticipantId);
        Assert.Equal(2, run.CurrentRound);
        Assert.Equal([game[0].ParticipantId, game[1].ParticipantId], run.TurnOrder);
    }

    [Fact]
    public async Task RemovingANonActingPlayer_LeavesTheTurnAndTheRollAlone()
    {
        var game = await StartGameAsync("Alice", "Bob", "Cara");
        await SetPendingRollAsync(game, "Wisdom (Perception)");

        using var response = await RemoveAsync(game.Table, game[1].ParticipantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RemoveParticipantResult>(Web))!;
        Assert.Empty(result.TurnPassedInRunIds);
        Assert.Equal([game.RunId], result.AdventureRunIds);
        var run = await RunAsync(game);
        Assert.Equal([game[0].ParticipantId, game[2].ParticipantId], run.TurnOrder);
        Assert.Equal(game[0].ParticipantId, run.ActiveParticipantId);
        Assert.Equal("Wisdom (Perception)", run.PendingRollLabel);
    }

    [Fact]
    public async Task RemovingTheLastPlayer_LeavesAnEmptyGroupRun_ThatAnAddedPlayerPicksUp()
    {
        var game = await StartGameAsync("Alice");
        (await RemoveAsync(game.Table, game[0].ParticipantId)).EnsureSuccessStatusCode();

        // Nobody to pass to: a clear message, not "not a group run".
        using var pass = await AdvanceAsync(game);
        Assert.Equal(HttpStatusCode.BadRequest, pass.StatusCode);
        var passBody = await BodyAsync(pass);
        Assert.Contains("No one is in this adventure's turn order", passBody.GetProperty("detail").GetString());

        // A new player is approved and added: they get the turn, and can act.
        var dave = await JoinAsync(game.Table, "Dave");
        using var added = await AddToRunAsync(game, dave.ParticipantId);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var run = await RunAsync(game);
        Assert.True(run.IsGroup);
        Assert.Equal([dave.ParticipantId], run.TurnOrder);
        Assert.Equal(dave.ParticipantId, run.ActiveParticipantId);
        Assert.Contains(Events(game.Table, "TurnAdvanced"), c => c.PayloadJson.Contains(dave.ParticipantId.ToString()));

        using var daveClient = As(dave.Token);
        using var acted = await ActAsync(daveClient, game);
        Assert.Equal(HttpStatusCode.OK, acted.StatusCode);
    }

    [Fact]
    public async Task WithNobodyLeft_APlayerActionSaysThereIsNoActingPlayer()
    {
        var game = await StartGameAsync("Alice", "Bob");
        // Both players are removed; Cara is approved afterwards but not added to the run.
        (await RemoveAsync(game.Table, game[0].ParticipantId)).EnsureSuccessStatusCode();
        (await RemoveAsync(game.Table, game[1].ParticipantId)).EnsureSuccessStatusCode();
        var cara = await JoinAsync(game.Table, "Cara");   // approved, but not in the run
        using var caraClient = As(cara.Token);

        using var response = await ActAsync(caraClient, game);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("no-acting-player", CodeOf(body));
        Assert.Contains("add a player", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ALegacyDanglingActingId_Recovers_ThePassGoesToTheFirstInOrder_WithoutCountingARound()
    {
        var game = await StartGameAsync("Alice", "Bob", "Cara");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            var run = await db.AdventureSessions.SingleAsync(r => r.Id == game.RunId);
            run.TurnOrder = [game[1].ParticipantId, game[2].ParticipantId];   // Alice gone, still "acting"
            await db.SaveChangesAsync();
        }

        (await AdvanceAsync(game)).EnsureSuccessStatusCode();

        var after = await RunAsync(game);
        Assert.Equal(game[1].ParticipantId, after.ActiveParticipantId);
        Assert.Equal(1, after.CurrentRound);
    }

    [Fact]
    public async Task SomeoneNotInTheTurnOrder_IsRemovedFromTheSession_AndTheRunIsUntouched()
    {
        var game = await StartGameAsync("Alice", "Bob");
        var watcher = await JoinAsync(game.Table, "Wanda");   // approved, never added to the run
        var before = await RunAsync(game);

        using var response = await RemoveAsync(game.Table, watcher.ParticipantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RemoveParticipantResult>(Web))!;
        Assert.Empty(result.AdventureRunIds);
        var after = await RunAsync(game);
        Assert.Equal(before.TurnOrder, after.TurnOrder);
        Assert.Equal(before.ActiveParticipantId, after.ActiveParticipantId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. Who can be removed, and by whom
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task APendingRequest_IsDeniedNotRemoved()
    {
        var table = await StartTableAsync(requireHostApproval: true);
        var waiting = await JoinAsync(table, "Walt", approve: false);

        using var response = await RemoveAsync(table, waiting.ParticipantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Deny the request", (await BodyAsync(response)).GetProperty("detail").GetString());
        Assert.Null((await ParticipantAsync(waiting.ParticipantId)).RemovedAt);
    }

    [Fact]
    public async Task AnUnknownParticipant_Is404()
    {
        var table = await StartTableAsync();
        using var response = await RemoveAsync(table, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemovingTwice_IsHarmless_AndAnnouncedOnce()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        (await RemoveAsync(table, alice.ParticipantId)).EnsureSuccessStatusCode();

        using var again = await RemoveAsync(table, alice.ParticipantId);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(Events(table, "ParticipantRemoved"));
    }

    [Fact]
    public async Task ADeniedRequest_CannotBeRemoved_AndTheEndedSessionRefuses()
    {
        var table = await StartTableAsync(requireHostApproval: true);
        var denied = await JoinAsync(table, "Mallory", approve: false);
        var alice = await JoinAsync(table, "Alice");
        using (var host = Host())
        {
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/deny-participant", new { participantId = denied.ParticipantId }))
                .EnsureSuccessStatusCode();
        }

        using var deniedRemoval = await RemoveAsync(table, denied.ParticipantId);
        Assert.Equal(HttpStatusCode.Conflict, deniedRemoval.StatusCode);

        using (var host = Host())
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/end", new { })).EnsureSuccessStatusCode();
        using var afterEnd = await RemoveAsync(table, alice.ParticipantId);
        Assert.Equal(HttpStatusCode.Conflict, afterEnd.StatusCode);
    }

    [Fact]
    public async Task OnlyTheHost_MayRemove()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        var bob = await JoinAsync(table, "Bob");
        using var bobClient = As(bob.Token);
        using var stranger = _factory.CreateClientFrom(LanAddress);

        using var byPlayer = await RemoveAsync(table, alice.ParticipantId, bobClient);
        using var byStranger = await RemoveAsync(table, alice.ParticipantId, stranger);

        Assert.Equal(HttpStatusCode.Forbidden, byPlayer.StatusCode);
        Assert.True(byStranger.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Null((await ParticipantAsync(alice.ParticipantId)).RemovedAt);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4. What a removed person keeps: nothing
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ARemovedPlayersToken_IsRefused_WithTheRemovedCode()
    {
        var game = await StartGameAsync("Alice", "Bob");
        (await RemoveAsync(game.Table, game[1].ParticipantId)).EnsureSuccessStatusCode();
        using var bob = As(game[1].Token);

        using var run = await bob.GetAsync($"/api/sessions/{game.Table.Session.Id}/adventure-run");
        using var act = await ActAsync(bob, game);

        foreach (var response in new[] { run, act })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await BodyAsync(response);
            Assert.Equal("participant-removed", CodeOf(body));
            Assert.Equal("You were removed from this session", body.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task ARemovedBrowsersSecret_NeverMintsAToken_AndRejoinSaysRemoved()
    {
        var game = await StartGameAsync("Alice", "Bob");
        var bob = game[1];
        (await RemoveAsync(game.Table, bob.ParticipantId)).EnsureSuccessStatusCode();
        using var lan = _factory.CreateClientFrom(LanAddress);

        using var rejoin = await lan.PostAsJsonAsync($"/api/sessions/{game.Table.Session.Id}/rejoin", new { secret = bob.Secret });
        using var status = await lan.PostAsJsonAsync($"/api/sessions/{game.Table.Session.Id}/rejoin-status", new { secret = bob.Secret });

        foreach (var response in new[] { rejoin, status })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Equal("participant-removed", CodeOf(JsonDocument.Parse(text).RootElement));
            Assert.DoesNotContain("participantToken", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ARemovedBrowser_JoiningAgain_IsANewRequestTheHostMustApprove_EvenOnAnOpenTable()
    {
        var game = await StartGameAsync("Alice", "Bob");   // the table auto-approves
        var bob = game[1];
        (await RemoveAsync(game.Table, bob.ParticipantId)).EnsureSuccessStatusCode();

        using var lan = _factory.CreateClientFrom(LanAddress);
        using var joined = await lan.PostAsJsonAsync($"/api/sessions/{game.Table.Session.Id}/join",
            new { DisplayName = "Bob", InviteCode = game.Table.Session.InviteCode, RejoinSecret = bob.Secret });

        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var again = (await joined.Content.ReadFromJsonAsync<JoinSessionResult>(Web))!;
        Assert.NotEqual(bob.ParticipantId, again.ParticipantId);
        Assert.False(again.IsApproved);

        using var host = Host();
        var roster = (await host.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{game.Table.Session.Id}/participants", Web))!;
        var request = Assert.Single(roster, p => p.Id == again.ParticipantId);
        Assert.False(request.IsApproved);
        Assert.Null(request.SameNameAsParticipantId);   // never points at the removed seat
        Assert.DoesNotContain(roster, p => p.Id == bob.ParticipantId);
        Assert.Contains(Events(game.Table, "ParticipantJoined"), c => c.PayloadJson.Contains(again.ParticipantId.ToString()));

        // The old secret moved to the new request: the removed record no longer answers to it.
        Assert.Null((await ParticipantAsync(bob.ParticipantId)).RejoinSecretHash);
    }

    [Fact]
    public async Task RemovalUnassignsTheCharacter_AndTheRemovedRecordStaysOnFile()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        var characterId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            var p = await db.SessionParticipants.SingleAsync(x => x.Id == alice.ParticipantId);
            p.CharacterId = characterId;
            await db.SaveChangesAsync();
        }

        using var response = await RemoveAsync(table, alice.ParticipantId);

        var dto = (await response.Content.ReadFromJsonAsync<RemoveParticipantResult>(Web))!.Participant;
        Assert.True(dto.IsRemoved);
        Assert.Null(dto.CharacterId);
        var record = await ParticipantAsync(alice.ParticipantId);
        Assert.Null(record.CharacterId);
        Assert.False(record.IsApproved);
        Assert.NotNull(record.RemovedAt);

        using var host = Host();
        var roster = (await host.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{table.Session.Id}/participants", Web))!;
        Assert.DoesNotContain(roster, p => p.Id == alice.ParticipantId);
    }

    [Fact]
    public async Task Removal_TellsTheHostAndThePerson_TheTableHearsTheyLeft_AndEachRunChanged_WithNoSecrets()
    {
        var game = await StartGameAsync("Alice", "Bob");

        (await RemoveAsync(game.Table, game[0].ParticipantId)).EnsureSuccessStatusCode();

        var removed = Assert.Single(Events(game.Table, "ParticipantRemoved"));
        Assert.Contains(game[0].ParticipantId.ToString(), removed.PayloadJson);
        Assert.Contains(Events(game.Table, "ParticipantLeft"), c => c.PayloadJson.Contains(game[0].ParticipantId.ToString()));
        Assert.Contains(Events(game.Table, "AdventureStateChanged"), c => c.PayloadJson.Contains("ParticipantRemoved"));
        Assert.Contains(Events(game.Table, "TurnAdvanced"), c => c.PayloadJson.Contains(game[1].ParticipantId.ToString()));
        foreach (var call in _factory.Hub.For(game.Table.Session.Id).Where(c => c.Method is "ParticipantRemoved" or "ParticipantLeft"))
        {
            Assert.DoesNotContain(game[0].Token, call.PayloadJson);
            Assert.DoesNotContain(game[0].Secret!, call.PayloadJson);
            Assert.DoesNotContain("token", call.PayloadJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5. Presence is the host's, and is only a flag
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TheHostsRoster_SaysWhoIsConnected_APlayersDoesNot()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");

        using var host = Host();
        var hostText = await host.GetStringAsync($"/api/sessions/{table.Session.Id}/participants");
        using var aliceClient = As(alice.Token);
        var playerText = await aliceClient.GetStringAsync($"/api/sessions/{table.Session.Id}/participants");

        var hostRow = JsonDocument.Parse(hostText).RootElement.EnumerateArray().Single();
        Assert.Equal(JsonValueKind.False, hostRow.GetProperty("isConnected").ValueKind);   // no hub connection open
        var playerRow = JsonDocument.Parse(playerText).RootElement.EnumerateArray().Single();
        Assert.True(playerRow.GetProperty("isConnected").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        Assert.DoesNotContain("connectionId", hostText, StringComparison.OrdinalIgnoreCase);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 6. Races: the turn lock, shown with and without it
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Holds the removal of a run's participant at the point between its read and its write, so another request
    /// can be sent while it waits. With the lock that request waits too; without it, it reads and writes the old
    /// state, and one of the two writes is lost.
    /// </summary>
    private sealed class HeldRemoval : IDisposable
    {
        private readonly Guid _runId;
        public TaskCompletionSource RemovalReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool OtherWriterReached;

        public HeldRemoval(Guid runId, bool bypassLock)
        {
            _runId = runId;
            AdventureTurnLocks.SetTestHooks(runId, new AdventureTurnLocks.Hooks
            {
                Bypass = bypassLock,
                Checkpoint = async (writer, _) =>
                {
                    if (writer != "remove")
                    {
                        OtherWriterReached = true;
                        return;
                    }
                    RemovalReached.TrySetResult();
                    await Release.Task;
                },
            });
        }

        public void Dispose() => AdventureTurnLocks.ClearTestHooks(_runId);
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Remove Bob (not acting) while the host passes Alice's turn. Returns the run afterwards.</summary>
    private async Task<(Aircane.Domain.Entities.AdventureSession Run, Game Game, bool PassWaited)> RaceRemovalAgainstAdvanceAsync(bool bypassLock)
    {
        var game = await StartGameAsync("Alice", "Bob", "Cara");
        using var held = new HeldRemoval(game.RunId, bypassLock);

        var removal = RemoveAsync(game.Table, game[1].ParticipantId);
        await held.RemovalReached.Task.WaitAsync(Patience);
        var pass = AdvanceAsync(game);

        // With the lock the pass cannot even read the run while the removal holds it.
        var passWaited = await Task.WhenAny(pass, Task.Delay(400)) != pass && !held.OtherWriterReached;
        held.Release.TrySetResult();
        (await removal.WaitAsync(Patience)).EnsureSuccessStatusCode();
        (await pass.WaitAsync(Patience)).EnsureSuccessStatusCode();

        return (await RunAsync(game), game, passWaited);
    }

    [Fact]
    public async Task Race_RemovalAgainstAdvanceTurn_WithTheLock_BothChangesLand()
    {
        var (run, game, passWaited) = await RaceRemovalAgainstAdvanceAsync(bypassLock: false);

        Assert.True(passWaited, "The pass ran while the removal held the run.");
        // Removal first ([Alice, Cara], Alice acting), then the pass: Cara acts.
        Assert.Equal([game[0].ParticipantId, game[2].ParticipantId], run.TurnOrder);
        Assert.Equal(game[2].ParticipantId, run.ActiveParticipantId);
    }

    [Fact]
    public async Task Race_RemovalAgainstAdvanceTurn_WithoutTheLock_GoesWrong()
    {
        var (run, game, passWaited) = await RaceRemovalAgainstAdvanceAsync(bypassLock: true);

        Assert.False(passWaited);
        // The pass read the old order and gave the turn to Bob, who is being removed; the removal then wrote its own
        // stale view. Either the pass is lost or the acting id dangles: in neither case is Cara acting.
        Assert.NotEqual(game[2].ParticipantId, run.ActiveParticipantId);
    }

    /// <summary>Remove Alice (acting) while the host adds Dave to the run. Returns the run afterwards.</summary>
    private async Task<(Aircane.Domain.Entities.AdventureSession Run, Game Game, Guid Dave, bool AddWaited)> RaceRemovalAgainstAddAsync(bool bypassLock)
    {
        var game = await StartGameAsync("Alice", "Bob");
        var dave = await JoinAsync(game.Table, "Dave");
        using var held = new HeldRemoval(game.RunId, bypassLock);

        var removal = RemoveAsync(game.Table, game[0].ParticipantId);
        await held.RemovalReached.Task.WaitAsync(Patience);
        var add = AddToRunAsync(game, dave.ParticipantId);

        var addWaited = await Task.WhenAny(add, Task.Delay(400)) != add && !held.OtherWriterReached;
        held.Release.TrySetResult();
        (await removal.WaitAsync(Patience)).EnsureSuccessStatusCode();
        (await add.WaitAsync(Patience)).EnsureSuccessStatusCode();

        return (await RunAsync(game), game, dave.ParticipantId, addWaited);
    }

    [Fact]
    public async Task Race_RemovalAgainstAddParticipant_WithTheLock_NobodyIsLost()
    {
        var (run, game, dave, addWaited) = await RaceRemovalAgainstAddAsync(bypassLock: false);

        Assert.True(addWaited, "The add ran while the removal held the run.");
        Assert.Equal([game[1].ParticipantId, dave], run.TurnOrder);
        Assert.Equal(game[1].ParticipantId, run.ActiveParticipantId);
    }

    [Fact]
    public async Task Race_RemovalAgainstAddParticipant_WithoutTheLock_LosesTheNewPlayer()
    {
        var (run, _, dave, addWaited) = await RaceRemovalAgainstAddAsync(bypassLock: true);

        Assert.False(addWaited);
        Assert.DoesNotContain(dave, run.TurnOrder);
    }
}
