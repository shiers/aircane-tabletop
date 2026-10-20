using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.DTOs.Ai;
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
/// The desktop-mode backend (real participant tokens, a caller address the test picks) with the hub notifier
/// recorded and the AI replaced by one a test can script: answer, ask for a roll, or fail.
/// </summary>
public sealed class TurnEndAndChatFactory : DesktopModeWebApplicationFactory
{
    public HubRecorder Hub { get; } = new();

    public ScriptedAi Ai { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISessionHubNotifier>();
            services.AddSingleton(Hub.Notifier);
            services.RemoveAll<IPlayerActionService>();
            services.AddSingleton<IPlayerActionService>(sp =>
            {
                Ai.Services = sp.GetRequiredService<IServiceScopeFactory>();
                return Ai;
            });
        });
    }
}

/// <summary>An AI whose next answer a test decides.</summary>
public sealed class ScriptedAi : IPlayerActionService
{
    public IServiceScopeFactory? Services { get; set; }

    /// <summary>Set to make the AI call fail.</summary>
    public bool Fail { get; set; }

    /// <summary>Set to make the AI ask the acting player for this roll, as a labelled RequestRoll does.</summary>
    public string? AskForRoll { get; set; }

    public int Calls { get; private set; }

    public async Task<PlayerActionResponse> ProcessActionAsync(PlayerActionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Fail)
            throw new HttpRequestException("The AI provider is unreachable.");

        // The real service remembers a roll it asked for on the run (and clears it otherwise).
        using var scope = Services!.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IAdventureSessionService>();
        var run = await runs.GetActiveByCampaignAsync(request.CampaignId, cancellationToken);
        if (run is not null)
            await runs.UpdatePendingRollAsync(run.Id, AskForRoll, AskForRoll is null ? null : 14, cancellationToken);

        return new PlayerActionResponse("The torch flares.", PrivateDmNote: "DM ONLY: the door is trapped.", ProposedActions: [], Citations: []);
    }
}

/// <summary>
/// Ending a turn, auto-advance and the stored chat history, through the token-validating pipeline.
/// </summary>
public sealed class TurnEndAndChatTests : IClassFixture<TurnEndAndChatFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string HiddenText = "DM ONLY";

    private readonly TurnEndAndChatFactory _factory;

    public TurnEndAndChatTests(TurnEndAndChatFactory factory)
    {
        _factory = factory;
        _factory.Ai.Fail = false;
        _factory.Ai.AskForRoll = null;
    }

    // ── Set-up ────────────────────────────────────────────────────────────────

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private HttpClient As(string token)
    {
        var client = _factory.CreateClientFrom(LanAddress);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Seat(Guid ParticipantId, string Token);

    private sealed record Table(Guid CampaignId, SessionDto Session);

    private async Task<Table> StartTableAsync(bool requireHostApproval = false)
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Turns", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = requireHostApproval }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();
        return new Table(campaign.Id, session!);
    }

    private async Task<Seat> JoinAsync(Table table, string name, bool approve = true)
    {
        using var lan = _factory.CreateClientFrom(LanAddress);
        var joined = (await (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = name, InviteCode = table.Session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
        if (approve && !joined.IsApproved)
        {
            using var host = Host();
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/approve-participant",
                new { ParticipantId = joined.ParticipantId })).EnsureSuccessStatusCode();
        }
        return new Seat(joined.ParticipantId, joined.ParticipantToken);
    }

    private async Task<Guid> SeedAdventureAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var scenes = new List<GeneratedScene>
        {
            new() { SceneId = "scene-1", Title = "The Cellar", Description = "Spoiler synopsis.", SceneType = "exploration", ConnectsTo = ["scene-2"], ReadAloudText = "Cold stone steps lead down." },
            new() { SceneId = "scene-2", Title = "The Vault", Description = "Beyond.", SceneType = "roleplay", ConnectsTo = [] },
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

    private sealed record Game(Table Table, Seat Alice, Seat Bob, Seat Cara, Guid RunId);

    /// <summary>Alice, Bob and Cara at an open table, in that turn order, with Alice up first.</summary>
    private async Task<Game> StartGameAsync(string? turnAdvance = null)
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        var bob = await JoinAsync(table, "Bob");
        var cara = await JoinAsync(table, "Cara");
        var adventureId = await SeedAdventureAsync();
        using var host = Host();
        using var started = await host.PostAsJsonAsync("/api/adventure-sessions/start-group", new
        {
            generatedAdventureId = adventureId,
            campaignId = table.CampaignId,
            participantIds = new[] { alice.ParticipantId, bob.ParticipantId, cara.ParticipantId },
            playSessionId = table.Session.Id,
            turnAdvance,
        });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var run = (await started.Content.ReadFromJsonAsync<AdventureSessionStateDto>(Web))!;
        return new Game(table, alice, bob, cara, run.Id);
    }

    private async Task<Guid?> ActiveAsync(Game game)
    {
        using var host = Host();
        var state = await host.GetFromJsonAsync<AdventureSessionStateDto>($"/api/adventure-sessions/{game.RunId}", Web);
        return state!.ActiveParticipantId;
    }

    private static Task<HttpResponseMessage> EndTurnAsync(HttpClient client, Game game, string query = "")
        => client.PostAsync($"/api/adventure-sessions/{game.RunId}/advance-turn{query}", null);

    private static Task<HttpResponseMessage> ActAsync(HttpClient client, Game game, string text = "I light a torch.")
        => client.PostAsJsonAsync($"/api/adventure-sessions/{game.RunId}/player-action", new { actionText = text });

    private async Task SetPendingRollAsync(Game game, string label)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdventureSessionService>().UpdatePendingRollAsync(game.RunId, label, 14);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1. The acting player ends their own turn
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActingPlayer_CanEndTheirOwnTurn_AndGetsTheTableViewBack()
    {
        var game = await StartGameAsync();
        await SetPendingRollAsync(game, "Dexterity (Stealth)");
        using var alice = As(game.Alice.Token);

        using var response = await EndTurnAsync(alice, game);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var view = JsonSerializer.Deserialize<PlayerAdventureRunDto>(body, Web)!;
        Assert.Equal(game.Bob.ParticipantId, view.ActiveParticipantId);
        Assert.Null(view.PendingRoll);
        Assert.DoesNotContain("Spoiler synopsis", body);   // the player-safe view, not the full run state
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));

        using var scope = _factory.Services.CreateScope();
        var run = await scope.ServiceProvider.GetRequiredService<AircaneDbContext>().AdventureSessions.AsNoTracking().SingleAsync(r => r.Id == game.RunId);
        Assert.Null(run.PendingRollLabel);   // the pending roll went with the turn
    }

    [Fact]
    public async Task EndingATurn_BroadcastsTheAdventureStateEvent_AndSaysWhoEndedIt()
    {
        var game = await StartGameAsync();
        using var alice = As(game.Alice.Token);

        (await EndTurnAsync(alice, game)).EnsureSuccessStatusCode();

        var changed = _factory.Hub.For(game.Table.Session.Id, "AdventureStateChanged");
        Assert.Contains(changed, c => c.PayloadJson.Contains("TurnAdvanced"));
        var turn = Assert.Single(_factory.Hub.For(game.Table.Session.Id, "TurnAdvanced"));
        Assert.Contains("\"Player\"", turn.PayloadJson);
        Assert.DoesNotContain(HiddenText, turn.PayloadJson);

        using var scope = _factory.Services.CreateScope();
        var logged = await scope.ServiceProvider.GetRequiredService<AircaneDbContext>().CampaignEvents.AsNoTracking()
            .Where(e => e.CampaignId == game.Table.CampaignId && e.EventType == "Adventure:TurnAdvanced").ToListAsync();
        Assert.Contains(logged, e => e.PayloadJson.Contains(game.Alice.ParticipantId.ToString()) && e.PayloadJson.Contains("\"passedBy\":\"Player\""));
    }

    [Fact]
    public async Task NonActingPlayer_CannotEndSomeoneElsesTurn()
    {
        var game = await StartGameAsync();
        using var bob = As(game.Bob.Token);

        using var response = await EndTurnAsync(bob, game);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Not your turn", problem.GetProperty("title").GetString());
        Assert.Equal(game.Alice.ParticipantId, await ActiveAsync(game));
    }

    [Fact]
    public async Task Host_CanPassAnyTurn()
    {
        var game = await StartGameAsync();
        using var host = Host();

        using var response = await EndTurnAsync(host, game);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));
    }

    [Fact]
    public async Task WaitingAndDeniedParticipants_CannotEndATurn()
    {
        var table = await StartTableAsync(requireHostApproval: true);
        var alice = await JoinAsync(table, "Alice");
        var waiting = await JoinAsync(table, "Wanda", approve: false);
        var denied = await JoinAsync(table, "Mallory", approve: false);
        using (var host = Host())
        {
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/deny-participant", new { ParticipantId = denied.ParticipantId })).EnsureSuccessStatusCode();
        }
        var adventureId = await SeedAdventureAsync();
        Guid runId;
        using (var host = Host())
        {
            var started = await host.PostAsJsonAsync("/api/adventure-sessions/start-group", new
            {
                generatedAdventureId = adventureId, campaignId = table.CampaignId,
                participantIds = new[] { alice.ParticipantId }, playSessionId = table.Session.Id,
            });
            runId = (await started.Content.ReadFromJsonAsync<AdventureSessionStateDto>(Web))!.Id;
        }

        using var asWaiting = As(waiting.Token);
        using var waitingResponse = await asWaiting.PostAsync($"/api/adventure-sessions/{runId}/advance-turn", null);
        Assert.Equal(HttpStatusCode.Forbidden, waitingResponse.StatusCode);
        Assert.Equal("Waiting for approval", (await waitingResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        using var asDenied = As(denied.Token);
        using var deniedResponse = await asDenied.PostAsync($"/api/adventure-sessions/{runId}/advance-turn", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.Equal("participant-denied", (await deniedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PlayerOfAnotherSession_CannotEndTheTurn()
    {
        var game = await StartGameAsync();
        var elsewhere = await StartTableAsync();
        var stranger = await JoinAsync(elsewhere, "Eve");
        using var client = As(stranger.Token);

        using var response = await EndTurnAsync(client, game);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(game.Alice.ParticipantId, await ActiveAsync(game));
    }

    // ── A request that arrives after the turn moved on ───────────────────────

    [Fact]
    public async Task PlayersLateRequest_AfterTheHostPassed_IsA403_AndDoesNotSkipBob()
    {
        var game = await StartGameAsync();
        using (var host = Host())
            (await EndTurnAsync(host, game)).EnsureSuccessStatusCode();   // Alice -> Bob
        using var alice = As(game.Alice.Token);

        using var late = await EndTurnAsync(alice, game);

        Assert.Equal(HttpStatusCode.Forbidden, late.StatusCode);
        Assert.Contains("Waiting for Bob", (await late.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));
    }

    [Fact]
    public async Task HostsStaleRequest_WithFromParticipantId_Is409_AndNothingMoves()
    {
        var game = await StartGameAsync();
        using (var alice = As(game.Alice.Token))
            (await EndTurnAsync(alice, game)).EnsureSuccessStatusCode();   // Alice ended her turn: Bob's now
        using var host = Host();

        using var stale = await EndTurnAsync(host, game, $"?fromParticipantId={game.Alice.ParticipantId}");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));

        // The same request with the right participant goes through.
        using var current = await EndTurnAsync(host, game, $"?fromParticipantId={game.Bob.ParticipantId}");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(game.Cara.ParticipantId, await ActiveAsync(game));
    }

    [Fact]
    public async Task TwoSimultaneousRequestsForTheSameTurn_MoveItOnce()
    {
        var game = await StartGameAsync();
        using var alice = As(game.Alice.Token);
        using var aliceAgain = As(game.Alice.Token);

        var responses = await Task.WhenAll(
            Task.Run(() => EndTurnAsync(alice, game)),
            Task.Run(() => EndTurnAsync(aliceAgain, game)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden));
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));   // Bob, not Cara
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. Auto-advance
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StartGroup_AcceptsTheSettingByName_AndTheRunStateReportsIt()
    {
        var game = await StartGameAsync("AfterEachAction");
        using var host = Host();

        var raw = await host.GetStringAsync($"/api/adventure-sessions/{game.RunId}");

        Assert.Contains("\"turnAdvance\":\"AfterEachAction\"", raw);
        using var alice = As(game.Alice.Token);
        Assert.Contains("\"turnAdvance\":\"AfterEachAction\"", await alice.GetStringAsync($"/api/sessions/{game.Table.Session.Id}/adventure-run"));
    }

    [Fact]
    public async Task StartGroup_WithoutTheSetting_IsManualOnly()
    {
        var game = await StartGameAsync();
        using var host = Host();

        Assert.Contains("\"turnAdvance\":\"ManualOnly\"", await host.GetStringAsync($"/api/adventure-sessions/{game.RunId}"));
    }

    [Fact]
    public async Task StartGroup_WithAnUnknownSetting_IsRefused()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        var adventureId = await SeedAdventureAsync();
        using var host = Host();

        using var response = await host.PostAsJsonAsync("/api/adventure-sessions/start-group", new
        {
            generatedAdventureId = adventureId, campaignId = table.CampaignId,
            participantIds = new[] { alice.ParticipantId }, turnAdvance = "Sometimes",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AfterEachAction_TheTurnMovesOnOnceTheAiAnswered()
    {
        var game = await StartGameAsync("AfterEachAction");
        using var alice = As(game.Alice.Token);

        using var response = await ActAsync(alice, game);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("The torch flares.", raw);
        Assert.DoesNotContain(HiddenText, raw);   // a Player never gets the private DM note
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));
        Assert.Contains("\"Auto\"", Assert.Single(_factory.Hub.For(game.Table.Session.Id, "TurnAdvanced")).PayloadJson);
    }

    [Fact]
    public async Task ManualOnly_TheTurnStaysUntilSomeoneEndsIt()
    {
        var game = await StartGameAsync();
        using var alice = As(game.Alice.Token);

        (await ActAsync(alice, game)).EnsureSuccessStatusCode();

        Assert.Equal(game.Alice.ParticipantId, await ActiveAsync(game));
        Assert.Empty(_factory.Hub.For(game.Table.Session.Id, "TurnAdvanced"));
    }

    [Fact]
    public async Task AfterEachAction_AFailedAiCall_LeavesTheTurnWhereItWas_AndAliceCanTryAgain()
    {
        var game = await StartGameAsync("AfterEachAction");
        using var alice = As(game.Alice.Token);
        _factory.Ai.Fail = true;

        try
        {
            using var failed = await ActAsync(alice, game);
            Assert.True((int)failed.StatusCode >= 500, $"Expected a server error, got {(int)failed.StatusCode}.");
        }
        catch (HttpRequestException)
        {
            // The test server surfaces an unhandled provider failure to the caller as the exception itself.
        }

        Assert.Equal(game.Alice.ParticipantId, await ActiveAsync(game));
        Assert.Empty(_factory.Hub.For(game.Table.Session.Id, "TurnAdvanced"));

        _factory.Ai.Fail = false;
        (await ActAsync(alice, game)).EnsureSuccessStatusCode();
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));
    }

    [Fact]
    public async Task AfterEachAction_WhenTheAiAsksForARoll_TheTurnStaysUntilTheResultIsReported()
    {
        var game = await StartGameAsync("AfterEachAction");
        using var alice = As(game.Alice.Token);
        _factory.Ai.AskForRoll = "Dexterity (Stealth) check";

        (await ActAsync(alice, game, "I sneak past the guard.")).EnsureSuccessStatusCode();
        Assert.Equal(game.Alice.ParticipantId, await ActiveAsync(game));   // still hers: the roll is owed
        Assert.Contains("Dexterity (Stealth) check", await alice.GetStringAsync($"/api/sessions/{game.Table.Session.Id}/adventure-run"));

        _factory.Ai.AskForRoll = null;   // the next answer resolves the roll
        (await ActAsync(alice, game, "I rolled a 15.")).EnsureSuccessStatusCode();
        Assert.Equal(game.Bob.ParticipantId, await ActiveAsync(game));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. Chat history
    // ═════════════════════════════════════════════════════════════════════════

    private async Task SayAsync(Guid sessionId, string text, string name = "Alice", ParticipantRole role = ParticipantRole.Player)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IChatService>().RecordAsync(sessionId, Guid.NewGuid(), name, role, text);
    }

    [Fact]
    public async Task ApprovedParticipant_ReadsTheHistory_OldestFirst_WithSenderAndRole()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        await SayAsync(table.Session.Id, "hello");
        await SayAsync(table.Session.Id, "welcome", name: "Host", role: ParticipantRole.Host);
        using var client = As(alice.Token);

        using var response = await client.GetAsync($"/api/sessions/{table.Session.Id}/chat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var messages = page.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["hello", "welcome"], messages.Select(m => m.GetProperty("text").GetString()));
        Assert.Equal("Host", messages[1].GetProperty("senderName").GetString());
        Assert.Equal((int)ParticipantRole.Host, messages[1].GetProperty("senderRole").GetInt32());
        Assert.True(messages[0].TryGetProperty("sentAt", out _));
        Assert.False(page.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task TheHostOnTheTrustedMachine_ReadsTheHistory()
    {
        var table = await StartTableAsync();
        await SayAsync(table.Session.Id, "hello");
        using var host = Host();

        var page = await host.GetFromJsonAsync<JsonElement>($"/api/sessions/{table.Session.Id}/chat");

        Assert.Equal(1, page.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task History_DefaultsTo100_PagesBackWithBefore_AndReportsHasMore()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");
        for (var i = 1; i <= 120; i++)
            await SayAsync(table.Session.Id, $"m{i:000}");
        using var client = As(alice.Token);

        var newest = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{table.Session.Id}/chat");
        Assert.Equal(100, newest.GetProperty("messages").GetArrayLength());
        Assert.True(newest.GetProperty("hasMore").GetBoolean());
        Assert.Equal("m021", newest.GetProperty("messages")[0].GetProperty("text").GetString());
        Assert.Equal("m120", newest.GetProperty("messages")[99].GetProperty("text").GetString());

        var firstId = newest.GetProperty("messages")[0].GetProperty("id").GetGuid();
        var older = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{table.Session.Id}/chat?before={firstId}&limit=50");
        Assert.Equal(20, older.GetProperty("messages").GetArrayLength());
        Assert.False(older.GetProperty("hasMore").GetBoolean());
        Assert.Equal("m001", older.GetProperty("messages")[0].GetProperty("text").GetString());
        Assert.Equal("m020", older.GetProperty("messages")[19].GetProperty("text").GetString());

        var small = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{table.Session.Id}/chat?limit=5");
        Assert.Equal(5, small.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task WaitingParticipant_Gets403WaitingForApproval_OnTheHistory()
    {
        var table = await StartTableAsync(requireHostApproval: true);
        var waiting = await JoinAsync(table, "Wanda", approve: false);
        await SayAsync(table.Session.Id, "secret table talk");
        using var client = As(waiting.Token);

        using var response = await client.GetAsync($"/api/sessions/{table.Session.Id}/chat");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("Waiting for approval", raw);
        Assert.DoesNotContain("secret table talk", raw);
    }

    [Fact]
    public async Task DeniedParticipant_Gets403WithTheParticipantDeniedCode_OnTheHistory()
    {
        var table = await StartTableAsync(requireHostApproval: true);
        var mallory = await JoinAsync(table, "Mallory", approve: false);
        using (var host = Host())
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/deny-participant", new { ParticipantId = mallory.ParticipantId })).EnsureSuccessStatusCode();
        await SayAsync(table.Session.Id, "secret table talk");
        using var client = As(mallory.Token);

        using var response = await client.GetAsync($"/api/sessions/{table.Session.Id}/chat");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("participant-denied", raw);
        Assert.DoesNotContain("secret table talk", raw);
    }

    [Fact]
    public async Task AParticipantOfAnotherSession_CannotReadTheHistory()
    {
        var a = await StartTableAsync();
        var b = await StartTableAsync();
        var eve = await JoinAsync(a, "Eve");
        await SayAsync(b.Session.Id, "table B only");
        using var client = As(eve.Token);

        using var response = await client.GetAsync($"/api/sessions/{b.Session.Id}/chat");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("table B only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NoToken_FromTheLan_CannotReadTheHistory()
    {
        var table = await StartTableAsync();
        using var lan = _factory.CreateClientFrom(LanAddress);

        using var response = await lan.GetAsync($"/api/sessions/{table.Session.Id}/chat");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSession_IsA404_ForTheHost()
    {
        using var host = Host();

        using var response = await host.GetAsync($"/api/sessions/{Guid.NewGuid()}/chat");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
