using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Aircane.Application.Abstractions;
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
/// The desktop-mode backend (real participant tokens, a caller address the test picks) with the hub
/// notifier replaced by a recorder, so a test can see which events a request sent to a session's group,
/// and exactly what each carried.
/// </summary>
public sealed class GroupLobbyWebApplicationFactory : DesktopModeWebApplicationFactory
{
    public HubRecorder Hub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISessionHubNotifier>();
            services.AddSingleton(Hub.Notifier);
        });
    }
}

/// <summary>
/// Records every call made on an <see cref="ISessionHubNotifier"/>: the event method, the group it went to,
/// and the payload as JSON (the same shape SignalR would put on the wire).
/// </summary>
public sealed class HubRecorder
{
    public sealed record Call(string Method, Guid Group, string PayloadJson);

    private readonly List<Call> _calls = [];

    public ISessionHubNotifier Notifier { get; }

    public HubRecorder()
    {
        var proxy = DispatchProxy.Create<ISessionHubNotifier, RecordingProxy>();
        ((RecordingProxy)(object)proxy).Owner = this;
        Notifier = proxy;
    }

    public IReadOnlyList<Call> For(Guid group)
    {
        lock (_calls)
            return _calls.Where(c => c.Group == group).ToList();
    }

    public IReadOnlyList<Call> For(Guid group, string method) => For(group).Where(c => c.Method == method).ToList();

    private void Record(Call call)
    {
        lock (_calls)
            _calls.Add(call);
    }

    public class RecordingProxy : DispatchProxy
    {
        public HubRecorder? Owner { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var payload = (args ?? []).Skip(1).Where(a => a is not CancellationToken).ToArray();
            Owner!.Record(new Call(
                targetMethod!.Name.Replace("Notify", "").Replace("Async", ""),
                (Guid)args![0]!,
                JsonSerializer.Serialize(payload)));
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The group-lobby backend: start-group on a named play session, auto-approval, denying a join request,
/// the group hub events, and the player-safe adventure-run read. Everything goes through the real pipeline
/// with real participant tokens, since a test with an always-Host scheme could not tell a Player from the host.
/// </summary>
public sealed class GroupLobbyIntegrationTests : IClassFixture<GroupLobbyWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Text a Player must never see, planted in every place the AI's adventure keeps hidden content.
    private const string SceneSpoiler = "SPOILER-SCENE-DESCRIPTION";
    private const string DmNote = "SPOILER-DM-NOTES";
    private const string SecretText = "SPOILER-SECRET-CONTENT";
    private const string HiddenNpc = "SPOILER-NPC-NAME";
    private const string Objective = "SPOILER-OBJECTIVE";
    private const string NextScene = "SPOILER-NEXT-SCENE-TITLE";
    private const string ReadAloud = "The cellar door creaks open onto cold stone steps.";

    private static readonly string[] AllSpoilers = [SceneSpoiler, DmNote, SecretText, HiddenNpc, Objective, NextScene];

    private readonly GroupLobbyWebApplicationFactory _factory;

    public GroupLobbyIntegrationTests(GroupLobbyWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private HttpClient Lan(string? token = null)
    {
        var client = _factory.CreateClientFrom(LanAddress);
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> CreateCampaignAsync()
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Group Lobby", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        return campaign!.Id;
    }

    private async Task<SessionDto> StartSessionAsync(Guid campaignId, bool requireHostApproval)
    {
        using var host = Host();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaignId}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = requireHostApproval }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();
        return session!;
    }

    private async Task<JoinSessionResult> JoinAsync(SessionDto session, string name)
    {
        using var lan = Lan();
        var response = await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = name, InviteCode = session.InviteCode });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JoinSessionResult>())!;
    }

    private async Task<HttpResponseMessage> ApproveAsync(Guid sessionId, Guid participantId)
    {
        using var host = Host();
        return await host.PostAsJsonAsync($"/api/sessions/{sessionId}/approve-participant", new { ParticipantId = participantId });
    }

    private async Task<HttpResponseMessage> DenyAsync(Guid sessionId, Guid participantId, HttpClient? client = null)
    {
        var owned = client is null;
        client ??= Host();
        try
        {
            return await client.PostAsJsonAsync($"/api/sessions/{sessionId}/deny-participant", new { ParticipantId = participantId });
        }
        finally
        {
            if (owned)
                client.Dispose();
        }
    }

    private async Task<Guid> SeedAdventureAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();

        var scenes = new List<GeneratedScene>
        {
            new()
            {
                SceneId = "scene-1", Title = "The Cellar", Description = SceneSpoiler, SceneType = "exploration",
                ConnectsTo = ["scene-2"], Objectives = [Objective], ReadAloudText = ReadAloud, DmNotes = DmNote,
            },
            new() { SceneId = "scene-2", Title = NextScene, Description = "Beyond.", SceneType = "roleplay", ConnectsTo = [] },
        };
        var npcs = new List<GeneratedNpc>
        {
            new() { Name = HiddenNpc, Role = "villain", Personality = "Cold", StatsSummary = "Cultist", SceneIds = ["scene-1"] },
        };
        var clues = new AdventureClues
        {
            Secrets = [new AdventureSecret { Title = "Trapdoor", Content = SecretText, SceneId = "scene-1", DiscoveryMethod = "Search" }],
            Handouts = [],
            FailForwardPaths = [],
        };

        var now = DateTime.UtcNow;
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var adventure = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "The Cellar Below",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = "{}",
            PitchJson = JsonSerializer.Serialize(new AdventurePitch { Title = "The Cellar Below", Hook = "h", Summary = "s" }, camel),
            ScenesJson = JsonSerializer.Serialize(scenes, camel),
            NpcsJson = JsonSerializer.Serialize(npcs, camel),
            CluesJson = JsonSerializer.Serialize(clues, camel),
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedAdventures.Add(adventure);
        await db.SaveChangesAsync();
        return adventure.Id;
    }

    private async Task<HttpResponseMessage> StartGroupAsync(
        Guid adventureId, Guid campaignId, IEnumerable<Guid> participants, Guid? playSessionId = null)
    {
        using var host = Host();
        return await host.PostAsJsonAsync("/api/adventure-sessions/start-group",
            new { generatedAdventureId = adventureId, campaignId, participantIds = participants, playSessionId });
    }

    /// <summary>A campaign with an open session, two approved players and a running group run.</summary>
    private sealed record Table(
        Guid CampaignId, SessionDto Session, JoinSessionResult First, JoinSessionResult Second, Guid RunId, Guid AdventureId);

    private async Task<Table> SetUpRunningTableAsync()
    {
        var campaignId = await CreateCampaignAsync();
        var session = await StartSessionAsync(campaignId, requireHostApproval: false);
        var first = await JoinAsync(session, "Alice");
        var second = await JoinAsync(session, "Bob");
        var adventureId = await SeedAdventureAsync();

        using var started = await StartGroupAsync(adventureId, campaignId, [first.ParticipantId, second.ParticipantId], session.Id);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var run = (await started.Content.ReadFromJsonAsync<AdventureSessionDto>(Web))!;
        return new Table(campaignId, session, first, second, run.Id, adventureId);
    }

    private static void AssertNoSpoilers(string json, params string[] alsoForbidden)
    {
        foreach (var secret in AllSpoilers.Concat(alsoForbidden))
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
    }

    // ── start-group: playSessionId ────────────────────────────────────────────

    [Fact]
    public async Task StartGroup_WithPlaySessionId_BindsTheRunToThatSession()
    {
        var campaignId = await CreateCampaignAsync();
        var older = await StartSessionAsync(campaignId, requireHostApproval: false);
        var newer = await StartSessionAsync(campaignId, requireHostApproval: false);
        var player = await JoinAsync(older, "Alice"); // joined the OLDER session, not the newest
        var adventureId = await SeedAdventureAsync();

        using var response = await StartGroupAsync(adventureId, campaignId, [player.ParticipantId], older.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<AdventureSessionDto>(Web);
        Assert.Equal(older.Id, run!.SessionId);
        Assert.NotEqual(newer.Id, run.SessionId);
    }

    [Fact]
    public async Task StartGroup_WithoutPlaySessionId_StillUsesTheNewestOpenSession()
    {
        var campaignId = await CreateCampaignAsync();
        await StartSessionAsync(campaignId, requireHostApproval: false);
        var newest = await StartSessionAsync(campaignId, requireHostApproval: false);
        var player = await JoinAsync(newest, "Alice");
        var adventureId = await SeedAdventureAsync();

        using var response = await StartGroupAsync(adventureId, campaignId, [player.ParticipantId]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(newest.Id, (await response.Content.ReadFromJsonAsync<AdventureSessionDto>(Web))!.SessionId);
    }

    [Fact]
    public async Task StartGroup_PlaySessionOfAnotherCampaign_Returns400WithMessage()
    {
        var campaignId = await CreateCampaignAsync();
        await StartSessionAsync(campaignId, requireHostApproval: false);
        var otherCampaign = await CreateCampaignAsync();
        var foreign = await StartSessionAsync(otherCampaign, requireHostApproval: false);
        var player = await JoinAsync(foreign, "Alice");
        var adventureId = await SeedAdventureAsync();

        using var response = await StartGroupAsync(adventureId, campaignId, [player.ParticipantId], foreign.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("different campaign", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StartGroup_EndedPlaySession_Returns400WithMessage()
    {
        var campaignId = await CreateCampaignAsync();
        var session = await StartSessionAsync(campaignId, requireHostApproval: false);
        var player = await JoinAsync(session, "Alice");
        var adventureId = await SeedAdventureAsync();
        using (var host = Host())
        {
            (await host.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null })).EnsureSuccessStatusCode();
        }

        using var response = await StartGroupAsync(adventureId, campaignId, [player.ParticipantId], session.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("has ended", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StartGroup_UnknownPlaySession_Returns404()
    {
        var campaignId = await CreateCampaignAsync();
        await StartSessionAsync(campaignId, requireHostApproval: false);
        var adventureId = await SeedAdventureAsync();

        using var response = await StartGroupAsync(adventureId, campaignId, [Guid.NewGuid()], Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartGroup_RunningInAnotherPlaySession_Returns409InsteadOfHandingBackThatRun()
    {
        var table = await SetUpRunningTableAsync();
        var second = await StartSessionAsync(table.CampaignId, requireHostApproval: false);
        var latecomer = await JoinAsync(second, "Carol");

        using var response = await StartGroupAsync(table.AdventureId, table.CampaignId, [latecomer.ParticipantId], second.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Naming the session the run really is in is the idempotent resume it always was.
        using var resumed = await StartGroupAsync(table.AdventureId, table.CampaignId,
            [table.First.ParticipantId, table.Second.ParticipantId], table.Session.Id);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(table.RunId, (await resumed.Content.ReadFromJsonAsync<AdventureSessionDto>(Web))!.Id);
    }

    // ── requireHostApproval ───────────────────────────────────────────────────

    [Fact]
    public async Task Join_WithoutHostApproval_IsApprovedOnJoin()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: false);

        var joined = await JoinAsync(session, "Alice");

        Assert.True(joined.IsApproved);
        using var lan = Lan(joined.ParticipantToken);
        using var reconnect = await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null);
        Assert.True((await reconnect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isApproved").GetBoolean());
    }

    [Fact]
    public async Task Join_WithHostApproval_WaitsForTheHost_AndReconnectingDoesNotChangeThat()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);

        var joined = await JoinAsync(session, "Alice");

        Assert.False(joined.IsApproved);
        using var lan = Lan(joined.ParticipantToken);
        using var reconnect = await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
        Assert.False((await reconnect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isApproved").GetBoolean());

        (await ApproveAsync(session.Id, joined.ParticipantId)).EnsureSuccessStatusCode();
        using var again = await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null);
        Assert.True((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isApproved").GetBoolean());
    }

    // ── deny-participant ──────────────────────────────────────────────────────

    [Fact]
    public async Task Deny_ByHost_DropsThemFromTheRoster()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var pending = await JoinAsync(session, "Mallory");

        using var response = await DenyAsync(session.Id, pending.ParticipantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ParticipantDto>(Web))!.IsDenied);
        using var host = Host();
        var roster = await host.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{session.Id}/participants", Web);
        Assert.DoesNotContain(roster!, p => p.Id == pending.ParticipantId);
    }

    [Fact]
    public async Task Deny_ByAPlayer_Returns403_AndChangesNothing()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var approvedPlayer = await JoinAsync(session, "Alice");
        (await ApproveAsync(session.Id, approvedPlayer.ParticipantId)).EnsureSuccessStatusCode();
        var pending = await JoinAsync(session, "Bob");

        using var asPlayer = Lan(approvedPlayer.ParticipantToken);
        using var response = await DenyAsync(session.Id, pending.ParticipantId, asPlayer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AircaneDbContext>()
            .SessionParticipants.AsNoTracking().SingleAsync(p => p.Id == pending.ParticipantId);
        Assert.False(saved.IsDenied);
    }

    [Fact]
    public async Task Deny_AnAlreadyApprovedParticipant_Returns409()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: false);
        var joined = await JoinAsync(session, "Alice");

        using var response = await DenyAsync(session.Id, joined.ParticipantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deny_UnknownParticipant_Returns404()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);

        using var response = await DenyAsync(session.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deny_Twice_IsIdempotent_AndNotifiesOnce()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var pending = await JoinAsync(session, "Mallory");

        using var first = await DenyAsync(session.Id, pending.ParticipantId);
        using var second = await DenyAsync(session.Id, pending.ParticipantId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(_factory.Hub.For(session.Id, "ParticipantDenied"));
    }

    [Fact]
    public async Task Approve_ADeniedParticipant_Returns409()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var pending = await JoinAsync(session, "Mallory");
        (await DenyAsync(session.Id, pending.ParticipantId)).EnsureSuccessStatusCode();

        using var response = await ApproveAsync(session.Id, pending.ParticipantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeniedToken_IsRefusedWithAClear403_NotABare401()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var pending = await JoinAsync(session, "Mallory");
        (await DenyAsync(session.Id, pending.ParticipantId)).EnsureSuccessStatusCode();

        using var lan = Lan(pending.ParticipantToken);
        using var response = await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("participant-denied", problem.GetProperty("code").GetString());
        Assert.Contains("declined", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task DeniedPlayer_CanJoinAgainWithTheSameInviteCode_AsANewRequest()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var first = await JoinAsync(session, "Mallory");
        (await DenyAsync(session.Id, first.ParticipantId)).EnsureSuccessStatusCode();

        var second = await JoinAsync(session, "Mallory");

        Assert.NotEqual(first.ParticipantId, second.ParticipantId);
        Assert.False(second.IsApproved);
        using var lan = Lan(second.ParticipantToken);
        using var reconnect = await lan.PostAsync($"/api/sessions/{session.Id}/reconnect", null);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
    }

    // ── hub events ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hub_JoinNeedingApproval_SendsParticipantJoined_AndNoSecrets()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);

        var joined = await JoinAsync(session, "Alice");

        var events = _factory.Hub.For(session.Id);
        var sent = Assert.Single(events, c => c.Method == "ParticipantJoined");
        Assert.DoesNotContain(events, c => c.Method == "ParticipantApproved");
        Assert.Contains(joined.ParticipantId.ToString(), sent.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alice", sent.PayloadJson);
        foreach (var call in events)
        {
            Assert.DoesNotContain(session.InviteCode!, call.PayloadJson);
            Assert.DoesNotContain(joined.ParticipantToken, call.PayloadJson);
        }
    }

    [Fact]
    public async Task Hub_JoinWithoutApproval_SendsJoined_AndAutoApproved()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: false);

        var joined = await JoinAsync(session, "Alice");

        Assert.Single(_factory.Hub.For(session.Id, "ParticipantJoined"));
        var approved = Assert.Single(_factory.Hub.For(session.Id, "ParticipantApproved"));
        var payload = JsonDocument.Parse(approved.PayloadJson).RootElement[0];
        Assert.Equal(joined.ParticipantId, payload.GetProperty("ParticipantId").GetGuid());
        Assert.True(payload.GetProperty("AutoApproved").GetBoolean());
        Assert.DoesNotContain(session.InviteCode!, approved.PayloadJson);
        Assert.DoesNotContain(joined.ParticipantToken, approved.PayloadJson);
    }

    [Fact]
    public async Task Hub_HostApproval_SendsParticipantApproved_NotAutoApproved_OncePerApproval()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var joined = await JoinAsync(session, "Alice");

        (await ApproveAsync(session.Id, joined.ParticipantId)).EnsureSuccessStatusCode();
        (await ApproveAsync(session.Id, joined.ParticipantId)).EnsureSuccessStatusCode(); // a second click

        var approved = Assert.Single(_factory.Hub.For(session.Id, "ParticipantApproved"));
        Assert.False(JsonDocument.Parse(approved.PayloadJson).RootElement[0].GetProperty("AutoApproved").GetBoolean());
    }

    [Fact]
    public async Task Hub_Deny_SendsParticipantDenied_AndNoSecrets()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: true);
        var pending = await JoinAsync(session, "Mallory");

        (await DenyAsync(session.Id, pending.ParticipantId)).EnsureSuccessStatusCode();

        var denied = Assert.Single(_factory.Hub.For(session.Id, "ParticipantDenied"));
        Assert.Contains(pending.ParticipantId.ToString(), denied.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(session.InviteCode!, denied.PayloadJson);
        Assert.DoesNotContain(pending.ParticipantToken, denied.PayloadJson);
    }

    [Fact]
    public async Task Hub_AdvanceTurn_SendsAdventureStateChanged_WithNoHiddenContent()
    {
        var table = await SetUpRunningTableAsync();

        using var host = Host();
        (await host.PostAsync($"/api/adventure-sessions/{table.RunId}/advance-turn", null)).EnsureSuccessStatusCode();

        var changed = Assert.Single(_factory.Hub.For(table.Session.Id, "AdventureStateChanged"));
        Assert.Contains("TurnAdvanced", changed.PayloadJson);
        AssertNoSpoilers(changed.PayloadJson, ReadAloud, table.Session.InviteCode!);
    }

    [Fact]
    public async Task Hub_AdvanceScene_SendsAdventureStateChanged_WithNoHiddenContent()
    {
        var table = await SetUpRunningTableAsync();

        using var host = Host();
        (await host.PostAsJsonAsync($"/api/adventure-sessions/{table.RunId}/advance-scene",
            new { targetSceneId = "scene-2" })).EnsureSuccessStatusCode();

        var changed = Assert.Single(_factory.Hub.For(table.Session.Id, "AdventureStateChanged"));
        Assert.Contains("SceneAdvanced", changed.PayloadJson);
        AssertNoSpoilers(changed.PayloadJson, ReadAloud, table.Session.InviteCode!);
    }

    [Fact]
    public async Task Hub_PlayerAction_SendsAdventureStateChanged_WithNoHiddenContent()
    {
        var table = await SetUpRunningTableAsync();

        using var asFirst = Lan(table.First.ParticipantToken);
        using var response = await asFirst.PostAsJsonAsync($"/api/adventure-sessions/{table.RunId}/player-action",
            new { actionText = "I open the door." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = _factory.Hub.For(table.Session.Id, "AdventureStateChanged");
        Assert.Contains(changed, c => c.PayloadJson.Contains("PlayerAction"));
        foreach (var call in changed)
            AssertNoSpoilers(call.PayloadJson, ReadAloud, table.Session.InviteCode!);
    }

    // ── GET /api/sessions/{id}/adventure-run ──────────────────────────────────

    [Fact]
    public async Task AdventureRun_ApprovedPlayer_GetsTheSceneAndTurn_AndNoDmOnlyContent()
    {
        var table = await SetUpRunningTableAsync();

        using var lan = Lan(table.First.ParticipantToken);
        using var response = await lan.GetAsync($"/api/sessions/{table.Session.Id}/adventure-run");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        AssertNoSpoilers(body, table.Session.InviteCode!, table.First.ParticipantToken);

        var run = JsonSerializer.Deserialize<PlayerAdventureRunDto>(body, Web)!;
        Assert.Equal(table.RunId, run.RunId);
        Assert.Equal("The Cellar Below", run.AdventureTitle);
        Assert.Equal("The Cellar", run.Scene.Title);
        Assert.Equal(ReadAloud, run.Scene.Description);
        Assert.Equal(1, run.CurrentSceneNumber);
        Assert.Equal(2, run.TotalScenes);
        Assert.Equal(table.First.ParticipantId, run.ActiveParticipantId);
        Assert.Equal("Alice", run.ActiveParticipantName);
        Assert.Equal(2, run.TurnOrder.Count);
    }

    [Fact]
    public async Task AdventureRun_AfterAPlayerAction_ShowsTheStoryButNothingPrivate()
    {
        var table = await SetUpRunningTableAsync();
        using (var asFirst = Lan(table.First.ParticipantToken))
        {
            (await asFirst.PostAsJsonAsync($"/api/adventure-sessions/{table.RunId}/player-action",
                new { actionText = "I open the door." })).EnsureSuccessStatusCode();
        }

        using var asSecond = Lan(table.Second.ParticipantToken);
        var body = await asSecond.GetStringAsync($"/api/sessions/{table.Session.Id}/adventure-run");

        AssertNoSpoilers(body, "PrivateDmNote");
        var run = JsonSerializer.Deserialize<PlayerAdventureRunDto>(body, Web)!;
        Assert.Contains(run.RecentNarration, e => e.Text.Contains("I open the door."));
    }

    [Fact]
    public async Task AdventureRun_Host_GetsTheSamePlayerView()
    {
        var table = await SetUpRunningTableAsync();

        using var host = Host();
        var body = await host.GetStringAsync($"/api/sessions/{table.Session.Id}/adventure-run");

        AssertNoSpoilers(body);
        Assert.Equal(table.RunId, JsonSerializer.Deserialize<PlayerAdventureRunDto>(body, Web)!.RunId);
    }

    [Fact]
    public async Task AdventureRun_NoRunInProgress_Returns404()
    {
        var session = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: false);
        var joined = await JoinAsync(session, "Alice");

        using var lan = Lan(joined.ParticipantToken);
        using var response = await lan.GetAsync($"/api/sessions/{session.Id}/adventure-run");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdventureRun_ParticipantStillWaitingForApproval_Returns403()
    {
        var campaignId = await CreateCampaignAsync();
        var gated = await StartSessionAsync(campaignId, requireHostApproval: true);
        var approved = await JoinAsync(gated, "Alice");
        (await ApproveAsync(gated.Id, approved.ParticipantId)).EnsureSuccessStatusCode();
        var waiting = await JoinAsync(gated, "Bob");
        using var started = await StartGroupAsync(await SeedAdventureAsync(), campaignId, [approved.ParticipantId], gated.Id);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        using var asWaiting = Lan(waiting.ParticipantToken);
        using var response = await asWaiting.GetAsync($"/api/sessions/{gated.Id}/adventure-run");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoSpoilers(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdventureRun_TokenOfAnotherSession_Returns403()
    {
        var table = await SetUpRunningTableAsync();
        var elsewhere = await StartSessionAsync(await CreateCampaignAsync(), requireHostApproval: false);
        var outsider = await JoinAsync(elsewhere, "Eve");

        using var lan = Lan(outsider.ParticipantToken);
        using var response = await lan.GetAsync($"/api/sessions/{table.Session.Id}/adventure-run");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoSpoilers(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdventureRun_NoToken_FromTheLan_Returns401()
    {
        var table = await SetUpRunningTableAsync();

        using var lan = Lan();
        using var response = await lan.GetAsync($"/api/sessions/{table.Session.Id}/adventure-run");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdventureRun_DeniedParticipantsToken_IsRefusedWithTheClear403()
    {
        var table = await SetUpRunningTableAsync();
        var gatedSession = await StartSessionAsync(table.CampaignId, requireHostApproval: true);
        var pending = await JoinAsync(gatedSession, "Mallory");
        (await DenyAsync(gatedSession.Id, pending.ParticipantId)).EnsureSuccessStatusCode();

        using var lan = Lan(pending.ParticipantToken);
        using var response = await lan.GetAsync($"/api/sessions/{gatedSession.Id}/adventure-run");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("participant-denied", problem.GetProperty("code").GetString());
    }

    // ── Who may read or act in a run (token-validating pipeline, end to end) ──

    /// <summary>A session that needs approval: Alice is approved and in the run; Bob is still waiting.</summary>
    private async Task<(Table Table, JoinSessionResult Waiting)> SetUpGatedTableAsync()
    {
        var campaignId = await CreateCampaignAsync();
        var session = await StartSessionAsync(campaignId, requireHostApproval: true);
        var alice = await JoinAsync(session, "Alice");
        (await ApproveAsync(session.Id, alice.ParticipantId)).EnsureSuccessStatusCode();
        var adventureId = await SeedAdventureAsync();
        using var started = await StartGroupAsync(adventureId, campaignId, [alice.ParticipantId], session.Id);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var run = (await started.Content.ReadFromJsonAsync<AdventureSessionDto>(Web))!;
        var waiting = await JoinAsync(session, "Bob");
        return (new Table(campaignId, session, alice, alice, run.Id, adventureId), waiting);
    }

    /// <summary>Every call a Player token can make about a run, with what it would return to the Host.</summary>
    private static IEnumerable<(string Name, Func<HttpClient, Table, Task<HttpResponseMessage>> Send)> RunCalls() =>
    [
        ("GET full state", (c, t) => c.GetAsync($"/api/adventure-sessions/{t.RunId}")),
        ("GET history", (c, t) => c.GetAsync($"/api/adventure-sessions/{t.RunId}/history")),
        ("GET available-scenes", (c, t) => c.GetAsync($"/api/adventure-sessions/{t.RunId}/available-scenes")),
        ("GET active", (c, t) => c.GetAsync($"/api/adventure-sessions/active?campaignId={t.CampaignId}")),
        ("POST recap", (c, t) => c.PostAsync($"/api/adventure-sessions/{t.RunId}/recap", null)),
        ("POST resume", (c, t) => c.PostAsync($"/api/adventure-sessions/{t.RunId}/resume", null)),
        ("POST advance-scene", (c, t) => c.PostAsJsonAsync($"/api/adventure-sessions/{t.RunId}/advance-scene", new { targetSceneId = "scene-2" })),
        ("POST abandon", (c, t) => c.PostAsync($"/api/adventure-sessions/{t.RunId}/abandon", null)),
        ("POST player-action", (c, t) => c.PostAsJsonAsync($"/api/adventure-sessions/{t.RunId}/player-action", new { actionText = "I look around." })),
        ("POST ai/player-action", (c, t) => c.PostAsJsonAsync($"/api/sessions/{t.Session.Id}/ai/player-action", new { characterId = Guid.NewGuid(), actionText = "I look around." })),
    ];

    [Fact]
    public async Task ParticipantWaitingForApproval_Gets403OnEveryRunEndpoint_AndNothingChanges()
    {
        var (table, waiting) = await SetUpGatedTableAsync();
        using var bob = Lan(waiting.ParticipantToken);

        foreach (var (name, send) in RunCalls())
        {
            using var response = await send(bob, table);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{name} returned {(int)response.StatusCode}.");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Waiting for approval", body);
            AssertNoSpoilers(body);
        }

        using var host = Host();
        var run = await host.GetFromJsonAsync<AdventureSessionStateDto>($"/api/adventure-sessions/{table.RunId}", Web);
        Assert.Equal("InProgress", run!.Status);
        Assert.Equal("scene-1", run.CurrentScene.SceneId);
    }

    [Fact]
    public async Task DeniedParticipant_GetsTheParticipantDeniedRefusal_OnEveryRunEndpoint()
    {
        var (table, waiting) = await SetUpGatedTableAsync();
        (await DenyAsync(table.Session.Id, waiting.ParticipantId)).EnsureSuccessStatusCode();
        using var bob = Lan(waiting.ParticipantToken);

        foreach (var (name, send) in RunCalls())
        {
            using var response = await send(bob, table);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{name} returned {(int)response.StatusCode}.");
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("participant-denied", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task ApprovedPlayer_IsRefusedTheFullRunAndTheHostsControls_ButMayActOnTheirTurn()
    {
        var (table, _) = await SetUpGatedTableAsync();
        using var alice = Lan(table.First.ParticipantToken);

        foreach (var (name, send) in RunCalls().Where(c => !c.Name.Contains("player-action")))
        {
            using var response = await send(alice, table);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{name} returned {(int)response.StatusCode}.");
        }

        using var acted = await alice.PostAsJsonAsync($"/api/adventure-sessions/{table.RunId}/player-action",
            new { actionText = "I look around." });
        Assert.Equal(HttpStatusCode.OK, acted.StatusCode);
        var body = await acted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("narration").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("privateDmNote").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("state").ValueKind);
        Assert.Equal(0, body.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task ApprovedPlayer_CanStillReadThePlayerSafeRun()
    {
        var (table, _) = await SetUpGatedTableAsync();
        using var alice = Lan(table.First.ParticipantToken);

        var json = await alice.GetStringAsync($"/api/sessions/{table.Session.Id}/adventure-run");

        AssertNoSpoilers(json);
        Assert.Equal(table.RunId, JsonSerializer.Deserialize<PlayerAdventureRunDto>(json, Web)!.RunId);
    }
}
