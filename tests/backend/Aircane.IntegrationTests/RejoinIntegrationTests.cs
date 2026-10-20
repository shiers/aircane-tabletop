using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.Configuration;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// The desktop-mode backend (real participant tokens) with the hub notifier recorded, and a rejoin rate limit high
/// enough for a whole test class from one address (<see cref="RejoinRateLimitTests"/> checks the limit itself).
/// </summary>
public sealed class RejoinWebApplicationFactory : DesktopModeWebApplicationFactory
{
    public HubRecorder Hub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISessionHubNotifier>();
            services.AddSingleton(Hub.Notifier);
            services.RemoveAll<RateLimitingSettings>();
            services.AddSingleton(new RateLimitingSettings { Rejoin = new FixedWindowLimit { PermitLimit = 100_000, WindowSeconds = 60 } });
        });
    }
}

/// <summary>
/// Coming back to a table: a closed tab or a new tab must not cost a player their seat, and a name plus the
/// invite code must never be enough to take someone's seat. Through the token-validating pipeline.
/// </summary>
public sealed class RejoinIntegrationTests : IClassFixture<RejoinWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.60");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly RejoinWebApplicationFactory _factory;

    public RejoinIntegrationTests(RejoinWebApplicationFactory factory) => _factory = factory;

    // ── Set-up ────────────────────────────────────────────────────────────────

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private HttpClient Lan() => _factory.CreateClientFrom(LanAddress);

    private HttpClient As(string token)
    {
        var client = Lan();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Table(Guid CampaignId, SessionDto Session);

    private async Task<Table> StartTableAsync()
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Rejoin", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Friday Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = true }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();
        return new Table(campaign.Id, session!);
    }

    private async Task<JoinSessionResult> JoinAsync(Table table, string name, string? rejoinSecret = null)
    {
        using var lan = Lan();
        return (await (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = name, InviteCode = table.Session.InviteCode, RejoinSecret = rejoinSecret })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
    }

    private async Task ApproveAsync(Table table, Guid participantId)
    {
        using var host = Host();
        (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/approve-participant",
            new { ParticipantId = participantId })).EnsureSuccessStatusCode();
    }

    private async Task<Guid> SeedAdventureAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "The Cellar", Description = "Spoiler.", SceneType = "exploration", ConnectsTo = [], ReadAloudText = "Cold stone steps." },
        };
        var now = DateTime.UtcNow;
        var adventure = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "The Cellar Below",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = "{}",
            PitchJson = JsonSerializer.Serialize(new AdventurePitch { Title = "The Cellar Below", Hook = "h", Summary = "s" }, camel),
            ScenesJson = JsonSerializer.Serialize(scenes, camel),
            NpcsJson = "[]",
            CluesJson = JsonSerializer.Serialize(new AdventureClues { Secrets = [], Handouts = [], FailForwardPaths = [] }, camel),
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedAdventures.Add(adventure);
        await db.SaveChangesAsync();
        return adventure.Id;
    }

    /// <summary>Starts a group adventure with these participants in this turn order.</summary>
    private async Task<Guid> StartRunAsync(Table table, params Guid[] participantIds)
    {
        var adventureId = await SeedAdventureAsync();
        using var host = Host();
        using var started = await host.PostAsJsonAsync("/api/adventure-sessions/start-group", new
        {
            generatedAdventureId = adventureId,
            campaignId = table.CampaignId,
            participantIds,
            playSessionId = table.Session.Id,
        });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        return (await started.Content.ReadFromJsonAsync<AdventureSessionStateDto>(Web))!.Id;
    }

    private async Task<AdventureSession> RunAsync(Guid runId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AircaneDbContext>().AdventureSessions.AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    private async Task<List<SessionParticipant>> ParticipantsAsync(Table table)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AircaneDbContext>().SessionParticipants.AsNoTracking()
            .Where(p => p.SessionId == table.Session.Id).OrderBy(p => p.JoinedAt).ToListAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Step 1: the problem as it was
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A player whose tab was closed joins again with the same name and invite code. That is (and stays) a new
    /// join request the host must approve, and the first participant keeps its place in the turn order.
    /// </summary>
    [Fact]
    public async Task JoiningAgainWithTheSameNameAndCode_MakesASecondParticipant_AndLeavesTheFirstInTheTurnOrder()
    {
        var table = await StartTableAsync();
        var first = await JoinAsync(table, "Bifur");
        await ApproveAsync(table, first.ParticipantId);
        var runId = await StartRunAsync(table, first.ParticipantId);

        var second = await JoinAsync(table, "Bifur");

        Assert.NotEqual(first.ParticipantId, second.ParticipantId);
        Assert.False(second.IsApproved);
        var participants = await ParticipantsAsync(table);
        Assert.Equal(2, participants.Count(p => p.DisplayName == "Bifur"));
        var run = await RunAsync(runId);
        Assert.Equal([first.ParticipantId], run.TurnOrder);
        Assert.Equal(first.ParticipantId, run.ActiveParticipantId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Name plus invite code never takes a seat
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NamePlusInviteCode_NeverTakesAnExistingSeat_AndTheHostSeesTheRepeatedName()
    {
        var table = await StartTableAsync();
        var first = await JoinAsync(table, "Bifur", NewSecret());
        await ApproveAsync(table, first.ParticipantId);
        var runId = await StartRunAsync(table, first.ParticipantId);

        // Same name (other case), same code, no secret: a new request, which can do nothing yet.
        var second = await JoinAsync(table, "BIFUR");

        Assert.NotEqual(first.ParticipantId, second.ParticipantId);
        Assert.False(second.IsApproved);
        using (var impostor = As(second.ParticipantToken))
        using (var act = await impostor.PostAsJsonAsync($"/api/adventure-sessions/{runId}/player-action", new { actionText = "I take the gold." }))
            Assert.Equal(HttpStatusCode.Forbidden, act.StatusCode);
        Assert.Equal([first.ParticipantId], (await RunAsync(runId)).TurnOrder);

        // The first seat is untouched: its token still works.
        using (var owner = As(first.ParticipantToken))
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/sessions/{table.Session.Id}/adventure-run")).StatusCode);

        // The host's roster flags the request, naming the participant already here.
        var roster = await HostRosterAsync(table);
        Assert.Equal(first.ParticipantId, roster.Single(p => p.Id == second.ParticipantId).SameNameAsParticipantId);
        Assert.Null(roster.Single(p => p.Id == first.ParticipantId).SameNameAsParticipantId);

        // And so does the ParticipantJoined event the host's lobby hears.
        var joined = _factory.Hub.For(table.Session.Id, "ParticipantJoined").Last();
        Assert.Contains(first.ParticipantId.ToString(), joined.PayloadJson);
    }

    [Fact]
    public async Task TheSameNameFlag_IsForTheHostOnly()
    {
        var table = await StartTableAsync();
        var first = await JoinAsync(table, "Bifur", NewSecret());
        await ApproveAsync(table, first.ParticipantId);
        await JoinAsync(table, "Bifur");

        using var player = As(first.ParticipantToken);
        var body = await player.GetStringAsync($"/api/sessions/{table.Session.Id}/participants");

        Assert.DoesNotContain(first.ParticipantId.ToString(), body.Replace($"\"id\":\"{first.ParticipantId}\"", ""));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Rejoin with the secret
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SameBrowserRejoin_KeepsApprovalCharacterAndTurnPosition_WithNoDuplicate()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var alice = await JoinAsync(table, "Alice", NewSecret());
        await ApproveAsync(table, alice.ParticipantId);
        var bifur = await JoinAsync(table, "Bifur", secret);
        await ApproveAsync(table, bifur.ParticipantId);
        var characterId = await AssignNewCharacterAsync(table, bifur.ParticipantId, "Bifur the Bold");
        var runId = await StartRunAsync(table, alice.ParticipantId, bifur.ParticipantId);
        var joinedEvents = _factory.Hub.For(table.Session.Id, "ParticipantJoined").Count;
        var participantsBefore = (await ParticipantsAsync(table)).Count;

        // The tab was closed: the token is gone, the secret is not.
        using var lan = Lan();
        using var response = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RejoinSessionResult>(body, Web)!;
        Assert.Equal(bifur.ParticipantId, result.ParticipantId);
        Assert.True(result.IsApproved);
        Assert.Equal("approved", result.Status);
        Assert.Equal(characterId, result.CharacterId);
        Assert.Equal("Friday Table", result.SessionName);
        Assert.NotEqual(bifur.ParticipantToken, result.ParticipantToken);
        Assert.DoesNotContain(secret, body);

        // No new participant, no new request, same turn position.
        Assert.Equal(participantsBefore, (await ParticipantsAsync(table)).Count);
        Assert.Equal(joinedEvents, _factory.Hub.For(table.Session.Id, "ParticipantJoined").Count);
        Assert.Equal([alice.ParticipantId, bifur.ParticipantId], (await RunAsync(runId)).TurnOrder);

        // The fresh token plays; the one it replaced is revoked.
        using (var fresh = As(result.ParticipantToken))
        {
            var view = await fresh.GetFromJsonAsync<PlayerAdventureRunDto>($"/api/sessions/{table.Session.Id}/adventure-run", Web);
            Assert.Equal(2, view!.TurnOrder.Count);
        }
        using (var stale = As(bifur.ParticipantToken))
            Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync($"/api/sessions/{table.Session.Id}/adventure-run")).StatusCode);

        // And the seat can be taken back again later (the secret is not used up).
        using var again = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task PendingRejoin_StaysPending_AndMakesNoNewRequest()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var waiting = await JoinAsync(table, "Nori", secret);
        var joinedEvents = _factory.Hub.For(table.Session.Id, "ParticipantJoined").Count;

        using var lan = Lan();
        var result = (await (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<RejoinSessionResult>(Web))!;

        Assert.Equal(waiting.ParticipantId, result.ParticipantId);
        Assert.False(result.IsApproved);
        Assert.Equal("pending", result.Status);
        Assert.Single(await ParticipantsAsync(table));
        Assert.Equal(joinedEvents, _factory.Hub.For(table.Session.Id, "ParticipantJoined").Count);

        // The host approves the same request, and the rejoined login is the one that plays.
        await ApproveAsync(table, waiting.ParticipantId);
        using var player = As(result.ParticipantToken);
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync($"/api/sessions/{table.Session.Id}/participants")).StatusCode);
    }

    [Fact]
    public async Task WrongMissingOrMalformedSecret_EndedSession_AndUnknownSession_AllGetTheSameAnswer()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var bifur = await JoinAsync(table, "Bifur", secret);
        await ApproveAsync(table, bifur.ParticipantId);
        var ended = await StartTableAsync();
        var endedSecret = NewSecret();
        await JoinAsync(ended, "Ori", endedSecret);
        using (var host = Host())
            (await host.PostAsJsonAsync($"/api/sessions/{ended.Session.Id}/end", new { })).EnsureSuccessStatusCode();

        var attempts = new (Guid Session, object? Body)[]
        {
            (table.Session.Id, new { secret = NewSecret() }),          // wrong
            (table.Session.Id, new { }),                               // missing
            (table.Session.Id, new { secret = "not a secret!" }),      // malformed
            (table.Session.Id, new { secret = "c2hvcnQ" }),            // too short
            (ended.Session.Id, new { secret = endedSecret }),          // ended
            (Guid.NewGuid(), new { secret }),                          // no such session
        };

        foreach (var path in new[] { "rejoin", "rejoin-status" })
        {
            var bodies = new HashSet<string>();
            foreach (var (sessionId, body) in attempts)
            {
                using var lan = Lan();
                using var response = await lan.PostAsJsonAsync($"/api/sessions/{sessionId}/{path}", body);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                bodies.Add(await response.Content.ReadAsStringAsync());
            }
            Assert.Single(bodies);   // nothing in the answer says which case it was
        }
    }

    [Fact]
    public async Task ADeniedParticipant_CannotRejoinAroundTheDenial()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var nori = await JoinAsync(table, "Nori", secret);
        using (var host = Host())
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/deny-participant", new { ParticipantId = nori.ParticipantId })).EnsureSuccessStatusCode();

        using var lan = Lan();
        using var rejoin = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret });
        Assert.Equal(HttpStatusCode.Forbidden, rejoin.StatusCode);
        var body = await rejoin.Content.ReadAsStringAsync();
        Assert.Contains("participant-denied", body);
        Assert.DoesNotContain("participantToken", body, StringComparison.OrdinalIgnoreCase);

        using var status = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin-status", new { secret });
        Assert.Equal("denied", (await status.Content.ReadFromJsonAsync<RejoinStatusResult>(Web))!.Status);

        // Joining again (even from the same browser) is a new request for the host to decide on.
        var again = await JoinAsync(table, "Nori", secret);
        Assert.NotEqual(nori.ParticipantId, again.ParticipantId);
        Assert.False(again.IsApproved);
        using var rejoinNew = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret });
        Assert.Equal(again.ParticipantId, (await rejoinNew.Content.ReadFromJsonAsync<RejoinSessionResult>(Web))!.ParticipantId);
    }

    [Fact]
    public async Task ASecondBrowserProfile_JoinsAsANewRequest()
    {
        var table = await StartTableAsync();
        var phone = await JoinAsync(table, "Bifur", NewSecret());
        await ApproveAsync(table, phone.ParticipantId);

        var laptop = await JoinAsync(table, "Bifur", NewSecret());   // another profile: another secret

        Assert.NotEqual(phone.ParticipantId, laptop.ParticipantId);
        Assert.False(laptop.IsApproved);
        using var owner = As(phone.ParticipantToken);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/sessions/{table.Session.Id}/participants")).StatusCode);
    }

    [Fact]
    public async Task TheStatusCheck_MintsNoToken()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var bifur = await JoinAsync(table, "Bifur", secret);
        await ApproveAsync(table, bifur.ParticipantId);
        var tokenIdBefore = (await ParticipantsAsync(table)).Single().TokenId;

        using var lan = Lan();
        using var response = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin-status", new { secret });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, body);
        var status = JsonSerializer.Deserialize<RejoinStatusResult>(body, Web)!;
        Assert.Equal(new RejoinStatusResult("Friday Table", "Bifur", "approved"), status);
        Assert.Equal(tokenIdBefore, (await ParticipantsAsync(table)).Single().TokenId);
        using var owner = As(bifur.ParticipantToken);   // the existing login is untouched
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/sessions/{table.Session.Id}/participants")).StatusCode);
    }

    [Fact]
    public async Task TheSecret_IsStoredOnlyAsAHash_AndIsNeverBroadcast()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var joinedBody = await JoinRawAsync(table, "Bifur", secret);

        Assert.DoesNotContain(secret, joinedBody);
        var stored = (await ParticipantsAsync(table)).Single().RejoinSecretHash;
        Assert.NotNull(stored);
        Assert.Equal(64, stored!.Length);
        Assert.DoesNotContain(secret, stored);
        Assert.All(_factory.Hub.For(table.Session.Id), call => Assert.DoesNotContain(secret, call.PayloadJson));
        Assert.All(_factory.Hub.For(table.Session.Id), call => Assert.DoesNotContain(stored, call.PayloadJson));
    }

    [Fact]
    public async Task JoiningWithASecretThatAlreadyHasASeat_IsRefused_AndAMalformedSecretIsRejected()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        await JoinAsync(table, "Bifur", secret);

        using var lan = Lan();
        using var twice = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = "Bifur", InviteCode = table.Session.InviteCode, RejoinSecret = secret });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        using var malformed = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = "Bifur", InviteCode = table.Session.InviteCode, RejoinSecret = "too-short" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Single(await ParticipantsAsync(table));
    }

    [Fact]
    public async Task EndingTheSession_RevokesEverySecret()
    {
        var table = await StartTableAsync();
        var secret = NewSecret();
        var bifur = await JoinAsync(table, "Bifur", secret);
        await ApproveAsync(table, bifur.ParticipantId);

        using (var host = Host())
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/end", new { })).EnsureSuccessStatusCode();

        Assert.All(await ParticipantsAsync(table), p => Assert.Null(p.RejoinSecretHash));
        using var lan = Lan();
        Assert.Equal(HttpStatusCode.NotFound, (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret })).StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Replace seat
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReplaceSeat_MovesTheSeatAndTheTurn_ClearsThePendingRoll_AndRevokesTheOldLogin()
    {
        var table = await StartTableAsync();
        var oldSecret = NewSecret();
        var ghost = await JoinAsync(table, "Bifur", oldSecret);
        await ApproveAsync(table, ghost.ParticipantId);
        var alice = await JoinAsync(table, "Alice", NewSecret());
        await ApproveAsync(table, alice.ParticipantId);
        var characterId = await AssignNewCharacterAsync(table, ghost.ParticipantId, "Bifur the Bold");
        var runId = await StartRunAsync(table, ghost.ParticipantId, alice.ParticipantId);   // the ghost is acting
        await SetPendingRollAsync(runId, "Dexterity (Stealth)");
        var newSecret = NewSecret();
        var back = await JoinAsync(table, "Bifur", newSecret);   // the same person, on a new device

        using var host = Host();
        using var response = await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/replace-participant",
            new { OldParticipantId = ghost.ParticipantId, NewParticipantId = back.ParticipantId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ReplaceParticipantResult>(Web))!;
        Assert.True(result.Participant.IsApproved);
        Assert.Equal(characterId, result.Participant.CharacterId);
        Assert.True(result.TookTheTurn);

        // The run: same place in the order, the turn, and no roll that was asked of someone else.
        var run = await RunAsync(runId);
        Assert.Equal([back.ParticipantId, alice.ParticipantId], run.TurnOrder);
        Assert.Equal(back.ParticipantId, run.ActiveParticipantId);
        Assert.Null(run.PendingRollLabel);

        // The roster: the old participant is gone, its seat (and character) are the new one's.
        var roster = await HostRosterAsync(table);
        Assert.DoesNotContain(roster, p => p.Id == ghost.ParticipantId);
        var old = (await ParticipantsAsync(table)).Single(p => p.Id == ghost.ParticipantId);
        Assert.Equal(back.ParticipantId, old.ReplacedByParticipantId);
        Assert.Null(old.CharacterId);
        Assert.Null(old.RejoinSecretHash);

        // The old login and secret are dead; the new player plays, and can rejoin with their own secret.
        using (var stale = As(ghost.ParticipantToken))
            Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync($"/api/sessions/{table.Session.Id}/adventure-run")).StatusCode);
        using var lan = Lan();
        Assert.Equal(HttpStatusCode.NotFound, (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret = oldSecret })).StatusCode);
        using (var player = As(back.ParticipantToken))
        {
            var view = await player.GetFromJsonAsync<PlayerAdventureRunDto>($"/api/sessions/{table.Session.Id}/adventure-run", Web);
            Assert.Equal(back.ParticipantId, view!.ActiveParticipantId);
        }
        var rejoined = (await (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/rejoin", new { secret = newSecret }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<RejoinSessionResult>(Web))!;
        Assert.Equal(back.ParticipantId, rejoined.ParticipantId);
        Assert.Equal(characterId, rejoined.CharacterId);

        // The existing events, with no secrets in them.
        var group = _factory.Hub.For(table.Session.Id);
        Assert.Contains(group, c => c.Method == "ParticipantApproved" && c.PayloadJson.Contains(back.ParticipantId.ToString()));
        Assert.Contains(group, c => c.Method == "ParticipantLeft" && c.PayloadJson.Contains(ghost.ParticipantId.ToString()));
        Assert.Contains(group, c => c.Method == "AdventureStateChanged" && c.PayloadJson.Contains(runId.ToString()));
        Assert.All(group, c => Assert.DoesNotContain(oldSecret, c.PayloadJson));
        Assert.All(group, c => Assert.DoesNotContain(newSecret, c.PayloadJson));
    }

    [Fact]
    public async Task ReplaceSeat_IsRefused_ForAnApprovedOrDeniedNewParticipant_OrAnOldOneWithNoSeat()
    {
        var table = await StartTableAsync();
        var seat = await JoinAsync(table, "Bifur", NewSecret());
        await ApproveAsync(table, seat.ParticipantId);
        var approved = await JoinAsync(table, "Alice", NewSecret());
        await ApproveAsync(table, approved.ParticipantId);
        var denied = await JoinAsync(table, "Bifur", NewSecret());
        using var host = Host();
        (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/deny-participant", new { ParticipantId = denied.ParticipantId })).EnsureSuccessStatusCode();
        var pending = await JoinAsync(table, "Bifur", NewSecret());
        var otherPending = await JoinAsync(table, "Nori", NewSecret());

        async Task<HttpStatusCode> ReplaceAsync(Guid oldId, Guid newId)
        {
            using var r = await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/replace-participant",
                new { OldParticipantId = oldId, NewParticipantId = newId });
            return r.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Conflict, await ReplaceAsync(seat.ParticipantId, approved.ParticipantId));   // new already approved
        Assert.Equal(HttpStatusCode.Conflict, await ReplaceAsync(seat.ParticipantId, denied.ParticipantId));     // new denied
        Assert.Equal(HttpStatusCode.Conflict, await ReplaceAsync(otherPending.ParticipantId, pending.ParticipantId)); // old has no seat
        Assert.Equal(HttpStatusCode.Conflict, await ReplaceAsync(pending.ParticipantId, pending.ParticipantId));
        Assert.Equal(HttpStatusCode.NotFound, await ReplaceAsync(seat.ParticipantId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, await ReplaceAsync(Guid.Empty, pending.ParticipantId));

        // Nothing changed.
        var participants = await ParticipantsAsync(table);
        Assert.True(participants.Single(p => p.Id == seat.ParticipantId).IsApproved);
        Assert.False(participants.Single(p => p.Id == pending.ParticipantId).IsApproved);
    }

    [Fact]
    public async Task ReplaceSeat_IsForTheHostOnly()
    {
        var table = await StartTableAsync();
        var seat = await JoinAsync(table, "Bifur", NewSecret());
        await ApproveAsync(table, seat.ParticipantId);
        var pending = await JoinAsync(table, "Bifur", NewSecret());

        using var player = As(seat.ParticipantToken);
        using var response = await player.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/replace-participant",
            new { OldParticipantId = seat.ParticipantId, NewParticipantId = pending.ParticipantId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await ParticipantsAsync(table)).Single(p => p.Id == pending.ParticipantId).IsApproved);
    }

    // ── More set-up ───────────────────────────────────────────────────────────

    /// <summary>A rejoin secret as a browser makes one: 128 random bits, base64url.</summary>
    private static string NewSecret()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> JoinRawAsync(Table table, string name, string secret)
    {
        using var lan = Lan();
        using var response = await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = name, InviteCode = table.Session.InviteCode, RejoinSecret = secret });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<List<ParticipantDto>> HostRosterAsync(Table table)
    {
        using var host = Host();
        return (await host.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{table.Session.Id}/participants", Web))!;
    }

    private async Task<Guid> AssignNewCharacterAsync(Table table, Guid participantId, string name)
    {
        Guid characterId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            var character = new Character(name, "D&D 5e 2014", "PHB", 3, "{}", "{}", table.CampaignId);
            db.Characters.Add(character);
            await db.SaveChangesAsync();
            characterId = character.Id;
        }
        using var host = Host();
        (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/assign-character",
            new { ParticipantId = participantId, CharacterId = characterId })).EnsureSuccessStatusCode();
        return characterId;
    }

    private async Task SetPendingRollAsync(Guid runId, string label)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdventureSessionService>().UpdatePendingRollAsync(runId, label, 14);
    }
}

/// <summary>The rejoin endpoints with a small limit, to see it bite on the LAN.</summary>
public sealed class TightRejoinLimitFactory : DesktopModeWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RateLimitingSettings>();
            services.AddSingleton(new RateLimitingSettings { Rejoin = new FixedWindowLimit { PermitLimit = 3, WindowSeconds = 60 } });
        });
    }
}

public sealed class RejoinRateLimitTests : IClassFixture<TightRejoinLimitFactory>
{
    private readonly TightRejoinLimitFactory _factory;

    public RejoinRateLimitTests(TightRejoinLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task TheRejoinEndpoints_AreRateLimited_EvenOnTheLan()
    {
        var sessionId = Guid.NewGuid();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var lan = _factory.CreateClientFrom(IPAddress.Parse("192.168.1.77"));
            // A different forwarding header each time must not buy a fresh allowance on the LAN.
            lan.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.0.0.{i}");
            var path = i % 2 == 0 ? "rejoin" : "rejoin-status";
            using var response = await lan.PostAsJsonAsync($"/api/sessions/{sessionId}/{path}", new { secret = "AAAAAAAAAAAAAAAAAAAAAA" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);

        // Another device on the LAN has its own allowance.
        using var other = _factory.CreateClientFrom(IPAddress.Parse("192.168.1.78"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/sessions/{sessionId}/rejoin", new { secret = "AAAAAAAAAAAAAAAAAAAAAA" })).StatusCode);
    }
}
