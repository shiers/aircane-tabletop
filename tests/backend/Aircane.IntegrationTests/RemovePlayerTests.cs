using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// The desktop-mode backend (real participant tokens, a caller address the test picks) with the hub notifier
/// recorded and the AI scripted, for taking a player out of a session.
/// </summary>
public sealed class RemovePlayerFactory : DesktopModeWebApplicationFactory
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

/// <summary>
/// The host takes a player out of a session: the seat, the token, the rejoin secret and the turn order, and what
/// a group run does when the acting player, or the last player, goes.
/// </summary>
public sealed partial class RemovePlayerTests : IClassFixture<RemovePlayerFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly RemovePlayerFactory _factory;

    public RemovePlayerTests(RemovePlayerFactory factory)
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

    private sealed record Seat(Guid ParticipantId, string Token, string? Secret = null);

    private sealed record Table(Guid CampaignId, SessionDto Session);

    private async Task<Table> StartTableAsync(bool requireHostApproval = false)
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Removals", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = requireHostApproval }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();
        return new Table(campaign.Id, session!);
    }

    /// <summary>A fresh rejoin secret, as a browser makes one: base64url of 16 random bytes.</summary>
    private static string NewSecret()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<Seat> JoinAsync(Table table, string name, bool approve = true, string? secret = null)
    {
        using var lan = _factory.CreateClientFrom(LanAddress);
        var joined = (await (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = name, InviteCode = table.Session.InviteCode, RejoinSecret = secret })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
        if (approve && !joined.IsApproved)
        {
            using var host = Host();
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/approve-participant",
                new { ParticipantId = joined.ParticipantId })).EnsureSuccessStatusCode();
        }
        return new Seat(joined.ParticipantId, joined.ParticipantToken, secret);
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

    private sealed record Game(Table Table, IReadOnlyList<Seat> Seats, Guid RunId)
    {
        public Seat this[int i] => Seats[i];
    }

    /// <summary>The named players at an open table, in that turn order, with the first up.</summary>
    private async Task<Game> StartGameAsync(params string[] names)
    {
        var table = await StartTableAsync();
        var seats = new List<Seat>();
        foreach (var name in names)
            seats.Add(await JoinAsync(table, name, secret: NewSecret()));
        var adventureId = await SeedAdventureAsync();
        using var host = Host();
        using var started = await host.PostAsJsonAsync("/api/adventure-sessions/start-group", new
        {
            generatedAdventureId = adventureId,
            campaignId = table.CampaignId,
            participantIds = seats.Select(s => s.ParticipantId).ToArray(),
            playSessionId = table.Session.Id,
        });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var run = (await started.Content.ReadFromJsonAsync<AdventureSessionStateDto>(Web))!;
        return new Game(table, seats, run.Id);
    }

    private async Task<AdventureSession> RunAsync(Game game)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        return await db.AdventureSessions.AsNoTracking().SingleAsync(r => r.Id == game.RunId);
    }

    private async Task<SessionParticipant> ParticipantAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        return await db.SessionParticipants.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private Task<HttpResponseMessage> RemoveAsync(Table table, Guid participantId, HttpClient? client = null)
    {
        var caller = client ?? Host();
        return caller.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/remove-participant", new { participantId });
    }

    private static Task<HttpResponseMessage> ActAsync(HttpClient client, Game game, string text = "I light a torch.")
        => client.PostAsJsonAsync($"/api/adventure-sessions/{game.RunId}/player-action", new { actionText = text });

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static string? CodeOf(JsonElement body)
        => body.TryGetProperty("code", out var code) ? code.GetString() : null;

    // ═════════════════════════════════════════════════════════════════════════
    // 1. The reported holes
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TheHost_CanRemoveAnApprovedPlayer()
    {
        var table = await StartTableAsync();
        var alice = await JoinAsync(table, "Alice");

        using var response = await RemoveAsync(table, alice.ParticipantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await ParticipantAsync(alice.ParticipantId)).RemovedAt);
    }

    [Fact]
    public async Task ALonePlayer_WhoHoldsTheTurn_CanBeTakenOut_AndTheRunStaysAGroupRun()
    {
        var game = await StartGameAsync("Alice");

        using var response = await RemoveAsync(game.Table, game[0].ParticipantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await RunAsync(game);
        Assert.True(run.IsGroup);
        Assert.Empty(run.TurnOrder);
        Assert.Null(run.ActiveParticipantId);
        Assert.Equal(AdventureSessionStatus.InProgress, run.Status);
    }

    [Fact]
    public async Task ADanglingActingParticipant_GivesPlayersAClearMessage_NotNotYourTurn()
    {
        var game = await StartGameAsync("Alice", "Bob", "Cara");
        // Alice leaves the turn order by some other means (a legacy run), still named as the acting player.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            var run = await db.AdventureSessions.SingleAsync(r => r.Id == game.RunId);
            run.TurnOrder = [game[1].ParticipantId, game[2].ParticipantId];
            await db.SaveChangesAsync();
        }
        using var bob = As(game[1].Token);

        using var response = await ActAsync(bob, game);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no-acting-player", CodeOf(await BodyAsync(response)));
    }
}
