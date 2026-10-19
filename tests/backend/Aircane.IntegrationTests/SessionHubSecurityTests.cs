using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// The session hub over real SignalR connections, with real participant tokens (the desktop-mode factory
/// validates them, including revocation) and the real notifier: who may join which group, whose name a
/// message carries, who hears which event, and whose connection is closed.
/// </summary>
public sealed class SessionHubSecurityTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");

    // The key the factory's participant token service signs with (see AircaneWebApplicationFactory).
    private const string SigningKey = "TestSigningKey_AtLeast32Characters_ForHMACSHA256!";

    /// <summary>How long to keep listening for something that must not arrive, after the control client got it.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly DesktopModeWebApplicationFactory _factory;

    public SessionHubSecurityTests(DesktopModeWebApplicationFactory factory)
    {
        _factory = factory;
        _ = _factory.CreateClient(); // start the server
    }

    // ── Table set-up over REST ────────────────────────────────────────────────

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private HttpClient Lan(string? token = null)
    {
        var client = _factory.CreateClientFrom(LanAddress);
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<SessionDto> StartSessionAsync(bool requireHostApproval = true)
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Hub Security", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        return (await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = requireHostApproval }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private async Task<JoinSessionResult> JoinAsync(SessionDto session, string name)
    {
        using var lan = Lan();
        return (await (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = name, InviteCode = session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
    }

    private async Task<JoinSessionResult> JoinApprovedAsync(SessionDto session, string name)
    {
        var joined = await JoinAsync(session, name);
        using var host = Host();
        (await host.PostAsJsonAsync($"/api/sessions/{session.Id}/approve-participant",
            new { ParticipantId = joined.ParticipantId })).EnsureSuccessStatusCode();
        return joined with { IsApproved = true };
    }

    private async Task DenyAsync(SessionDto session, Guid participantId)
    {
        using var host = Host();
        (await host.PostAsJsonAsync($"/api/sessions/{session.Id}/deny-participant",
            new { ParticipantId = participantId })).EnsureSuccessStatusCode();
    }

    private async Task NotifyAsync(Func<ISessionHubNotifier, Task> send)
    {
        using var scope = _factory.Services.CreateScope();
        await send(scope.ServiceProvider.GetRequiredService<ISessionHubNotifier>());
    }

    // ── Hub clients ───────────────────────────────────────────────────────────

    /// <summary>The events every probe listens for: everything the server can send to a session's clients.</summary>
    private static readonly string[] AllEvents =
    [
        "ParticipantJoined", "ParticipantLeft", "ParticipantApproved", "ParticipantDenied",
        "ChatMessageReceived", "RollRecorded", "RollRequested",
        "AINarrationStarted", "AINarrationChunk", "AINarrationCompleted", "AIProposalCreated",
        "StateUpdated", "SceneChanged", "HandoutRevealed", "CombatTurnChanged", "ImportStatusUpdated",
        "AdventureSceneAdvanced", "AdventureClueRevealed", "AdventureSecretRevealed", "AdventureNpcUpdated",
        "AdventureCompleted", "TurnAdvanced", "ParticipantJoinedAdventure", "AdventureStateChanged",
    ];

    /// <summary>A hub client that records every event it receives.</summary>
    private sealed class Probe : IAsyncDisposable
    {
        public HubConnection Connection { get; }
        public ConcurrentQueue<(string Event, JsonElement Payload)> Received { get; } = new();
        public TaskCompletionSource<Exception?> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _arrived = new(0);

        public Probe(HubConnection connection)
        {
            Connection = connection;
            foreach (var name in AllEvents)
            {
                connection.On<JsonElement>(name, payload =>
                {
                    Received.Enqueue((name, payload));
                    _arrived.Release();
                });
            }
            connection.Closed += ex =>
            {
                Closed.TrySetResult(ex);
                return Task.CompletedTask;
            };
        }

        public bool Got(string name) => Received.Any(r => r.Event == name);

        public IEnumerable<JsonElement> All(string name) => Received.Where(r => r.Event == name).Select(r => r.Payload);

        /// <summary>Waits for an event (optionally one matching a check) and returns its payload.</summary>
        public async Task<JsonElement> WaitFor(string name, Func<JsonElement, bool>? match = null)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                var hit = Received.Where(r => r.Event == name && (match is null || match(r.Payload))).Select(r => (JsonElement?)r.Payload).FirstOrDefault();
                if (hit is not null)
                    return hit.Value;
                await _arrived.WaitAsync(TimeSpan.FromMilliseconds(100));
            }
            throw new TimeoutException($"'{name}' never arrived. Got: {string.Join(", ", Received.Select(r => r.Event))}");
        }

        public async ValueTask DisposeAsync()
        {
            try { await Connection.DisposeAsync(); } catch { /* already closed */ }
        }
    }

    /// <summary>Opens a hub connection as a token holder, or (no token) from the host machine or a LAN device.</summary>
    private async Task<Probe> ConnectAsync(string? token, IPAddress? from = null, bool start = true)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/session"), options =>
            {
                // WebSockets, as a browser connects: one long-lived connection whose token is checked once,
                // when it opens. (Long polling re-sends the token on every poll, which would hide a
                // connection that outlives its participant's denial.)
                var remoteIp = (from ?? (token is null ? IPAddress.Loopback : LanAddress)).ToString();
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers[DesktopModeWebApplicationFactory.RemoteIpHeader] = remoteIp;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var client = _factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers[DesktopModeWebApplicationFactory.RemoteIpHeader] = remoteIp;
                        // A custom factory has to carry the token itself; the browser client sends it as
                        // ?access_token=, which the server also accepts on /hubs.
                        if (token is not null)
                            request.Headers.Authorization = $"Bearer {token}";
                    };
                    return await client.ConnectAsync(context.Uri, cancellationToken);
                };
                if (token is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var probe = new Probe(connection);
        if (start)
            await connection.StartAsync();
        return probe;
    }

    private static object JoinMessage(Guid sessionId, Guid participantId, string displayName, ParticipantRole role)
        => new { SessionId = sessionId, ParticipantId = participantId, DisplayName = displayName, Role = (int)role };

    private Task<Probe> HostOnHubAsync(SessionDto session) => JoinedAsync(null, session.Id, Guid.NewGuid(), "Host", ParticipantRole.Host);

    private async Task<Probe> JoinedAsync(string? token, Guid sessionId, Guid participantId, string name, ParticipantRole role = ParticipantRole.Player)
    {
        var probe = await ConnectAsync(token);
        await probe.Connection.InvokeAsync("JoinSession", JoinMessage(sessionId, participantId, name, role));
        return probe;
    }

    private static Task ChatAsync(Probe from, Guid sessionId, Guid participantId, string name, string text)
        => from.Connection.InvokeAsync("SendChatMessage", new { SessionId = sessionId, ParticipantId = participantId, DisplayName = name, Text = text });

    private static RollDto Roll(Guid sessionId, Guid rollerId, RollVisibility visibility, string context)
        => new(Guid.NewGuid(), sessionId, null, rollerId, "1d20", [17], 0, 17, false, visibility, context, DateTimeOffset.UtcNow);

    // ═════════════════════════════════════════════════════════════════════════
    // Step 1: the reported holes, each as the behaviour it must have
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>(a) A Player token for session A cannot join session B's group, so it hears nothing of B.</summary>
    [Fact]
    public async Task PlayerTokenForSessionA_CannotJoinSessionBsGroup()
    {
        var sessionA = await StartSessionAsync(requireHostApproval: false);
        var sessionB = await StartSessionAsync(requireHostApproval: false);
        var intruder = await JoinAsync(sessionA, "Mallory");
        await using var hostB = await HostOnHubAsync(sessionB);
        await using var playerB = await JoinedAsync((await JoinAsync(sessionB, "Bob")).ParticipantToken, sessionB.Id, Guid.NewGuid(), "Bob");
        await using var probe = await ConnectAsync(intruder.ParticipantToken);

        var refused = await Assert.ThrowsAsync<HubException>(() => probe.Connection.InvokeAsync(
            "JoinSession", JoinMessage(sessionB.Id, intruder.ParticipantId, "Mallory", ParticipantRole.Player)));
        Assert.Contains("different session", refused.Message);

        await ChatAsync(hostB, sessionB.Id, Guid.Empty, "Host", "secret plans for table B");
        await playerB.WaitFor("ChatMessageReceived");
        await Task.Delay(Quiet);
        Assert.False(probe.Got("ChatMessageReceived"), "A token for session A heard session B's chat.");
    }

    /// <summary>(b) A participant the host has not approved hears neither the table's chat nor its rolls.</summary>
    [Fact]
    public async Task WaitingParticipant_HearsNoChatAndNoRolls()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        var wanda = await JoinAsync(session, "Wanda");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        await using var wandaHub = await JoinedAsync(wanda.ParticipantToken, session.Id, wanda.ParticipantId, "Wanda");

        await ChatAsync(host, session.Id, Guid.Empty, "Host", "only for the table");
        await NotifyAsync(n => n.NotifyRollRecordedAsync(session.Id, Roll(session.Id, alice.ParticipantId, RollVisibility.Public, "Perception")));
        await aliceHub.WaitFor("ChatMessageReceived");
        await aliceHub.WaitFor("RollRecorded");
        await Task.Delay(Quiet);

        Assert.False(wandaHub.Got("ChatMessageReceived"), "A waiting participant heard the table's chat.");
        Assert.False(wandaHub.Got("RollRecorded"), "A waiting participant saw the table's rolls.");
    }

    /// <summary>(c) The sender of a chat message or a leave event is whoever holds the token, whatever the message says.</summary>
    [Fact]
    public async Task SpoofedSenderNameIdOrRole_IsIgnored()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        var bob = await JoinApprovedAsync(session, "Bob");
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        await using var bobHub = await JoinedAsync(bob.ParticipantToken, session.Id, bob.ParticipantId, "Bob");

        // Bob claims to be Alice...
        await ChatAsync(bobHub, session.Id, alice.ParticipantId, "Alice", "I give Bob all my gold.");
        var chat = await aliceHub.WaitFor("ChatMessageReceived");
        Assert.Equal(bob.ParticipantId, chat.GetProperty("participantId").GetGuid());
        Assert.Equal("Bob", chat.GetProperty("displayName").GetString());

        // ...and announces that Alice left.
        await bobHub.Connection.InvokeAsync("LeaveSession", session.Id, alice.ParticipantId, "Alice");
        var left = await aliceHub.WaitFor("ParticipantLeft");
        Assert.Equal(bob.ParticipantId, left.GetProperty("participantId").GetGuid());
        Assert.Equal("Bob", left.GetProperty("displayName").GetString());
    }

    /// <summary>(c) A waiting participant who claims the Host role in JoinSession gets nothing a host gets.</summary>
    [Fact]
    public async Task SpoofedHostRole_GetsNoHostOnlyEvents()
    {
        var session = await StartSessionAsync();
        var wanda = await JoinAsync(session, "Wanda");
        await using var host = await HostOnHubAsync(session);
        await using var wandaHub = await JoinedAsync(wanda.ParticipantToken, session.Id, Guid.Empty, "Host", ParticipantRole.Host);

        await NotifyAsync(n => n.NotifyAIProposalCreatedAsync(session.Id,
            new AIProposalCreatedNotification(session.Id, Guid.NewGuid(), "RevealSecret", "Reveal the cult leader", "{}")));
        await host.WaitFor("AIProposalCreated");
        await Task.Delay(Quiet);

        Assert.False(wandaHub.Got("AIProposalCreated"), "A participant claiming the Host role saw an AI proposal.");
    }

    /// <summary>(d) The host denies a waiting participant: their open connection is closed, and hears nothing more.</summary>
    [Fact]
    public async Task DeniedParticipant_ConnectionIsClosed()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        var mallory = await JoinAsync(session, "Mallory");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        await using var malloryHub = await JoinedAsync(mallory.ParticipantToken, session.Id, mallory.ParticipantId, "Mallory");

        await DenyAsync(session, mallory.ParticipantId);

        var closed = await Task.WhenAny(malloryHub.Closed.Task, Task.Delay(Patience));
        Assert.True(closed == malloryHub.Closed.Task, "The denied participant's connection stayed open.");

        await ChatAsync(host, session.Id, Guid.Empty, "Host", "after the denial");
        await aliceHub.WaitFor("ChatMessageReceived");
        await Task.Delay(Quiet);
        Assert.False(malloryHub.Got("ChatMessageReceived"), "A denied participant kept hearing the table.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Step 3: the rules, end to end
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WaitingParticipant_HearsOnlyTheirOwnDecision_AndJoinsTheTableWhenApproved()
    {
        var session = await StartSessionAsync();
        var wanda = await JoinAsync(session, "Wanda");
        var walt = await JoinAsync(session, "Walt");
        await using var host = await HostOnHubAsync(session);
        await using var wandaHub = await JoinedAsync(wanda.ParticipantToken, session.Id, wanda.ParticipantId, "Wanda");
        await using var waltHub = await JoinedAsync(walt.ParticipantToken, session.Id, walt.ParticipantId, "Walt");

        // Wanda is approved: she hears it, Walt (still waiting) does not.
        using (var rest = Host())
        {
            (await rest.PostAsJsonAsync($"/api/sessions/{session.Id}/approve-participant",
                new { ParticipantId = wanda.ParticipantId })).EnsureSuccessStatusCode();
        }
        var approved = await wandaHub.WaitFor("ParticipantApproved");
        Assert.Equal(wanda.ParticipantId, approved.GetProperty("participantId").GetGuid());

        // The server moved her open connection into the table's group: she now hears the table.
        await ChatAsync(host, session.Id, Guid.Empty, "Host", "welcome, Wanda");
        await wandaHub.WaitFor("ChatMessageReceived");
        await Task.Delay(Quiet);
        Assert.Empty(waltHub.Received);

        // Walt is denied: the host hears it, the table (Wanda) does not, and Walt's connection is closed.
        await DenyAsync(session, walt.ParticipantId);
        await host.WaitFor("ParticipantDenied", e => e.GetProperty("participantId").GetGuid() == walt.ParticipantId);
        Assert.True(await Task.WhenAny(waltHub.Closed.Task, Task.Delay(Patience)) == waltHub.Closed.Task);
        await Task.Delay(Quiet);
        Assert.False(wandaHub.Got("ParticipantDenied"), "The table was told about someone else's denial.");
        Assert.DoesNotContain(waltHub.Received, r => r.Event is "ChatMessageReceived" or "ParticipantApproved");
        Assert.True(waltHub.Got("ParticipantDenied"), "The denied participant was not told before being disconnected.");
    }

    [Fact]
    public async Task DeniedParticipant_CannotReconnect_WithTheirRevokedToken()
    {
        var session = await StartSessionAsync();
        var mallory = await JoinAsync(session, "Mallory");
        await using (var first = await JoinedAsync(mallory.ParticipantToken, session.Id, mallory.ParticipantId, "Mallory"))
        {
            await DenyAsync(session, mallory.ParticipantId);
            Assert.True(await Task.WhenAny(first.Closed.Task, Task.Delay(Patience)) == first.Closed.Task);
        }

        await using var again = await ConnectAsync(mallory.ParticipantToken, start: false);
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => again.Connection.StartAsync());
        Assert.Contains("403", refused.ToString());
    }

    [Fact]
    public async Task WaitingParticipant_CannotChat()
    {
        var session = await StartSessionAsync();
        var wanda = await JoinAsync(session, "Wanda");
        await using var host = await HostOnHubAsync(session);
        await using var wandaHub = await JoinedAsync(wanda.ParticipantToken, session.Id, wanda.ParticipantId, "Wanda");

        var refused = await Assert.ThrowsAsync<HubException>(() => ChatAsync(wandaHub, session.Id, wanda.ParticipantId, "Wanda", "let me in!"));

        Assert.Contains("once the host has let you in", refused.Message);
        await Task.Delay(Quiet);
        Assert.False(host.Got("ChatMessageReceived"));
    }

    /// <summary>Every event the server sends that a Player must not see reaches the host and no player.</summary>
    [Fact]
    public async Task HostOnlyEvents_ReachTheHost_AndNeverAPlayer()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        var run = Guid.NewGuid();

        await NotifyAsync(async n =>
        {
            await n.NotifyAIProposalCreatedAsync(session.Id, new AIProposalCreatedNotification(session.Id, Guid.NewGuid(), "RevealSecret", "Reveal the cult", "{}"));
            await n.NotifyStateUpdatedAsync(session.Id, "{\"npcs\":[{\"name\":\"Hidden assassin\",\"hp\":40}]}", DateTimeOffset.UtcNow);
            await n.NotifyImportStatusUpdatedAsync(session.Id, new ImportStatusUpdatedNotification(session.Id, Guid.NewGuid(), "Completed", 100, null));
            await n.NotifyAdventureClueRevealedAsync(session.Id, new AdventureClueRevealedNotification(run, "clue-1", "Letter", "Meet me below."));
            await n.NotifyAdventureSecretRevealedAsync(session.Id, new AdventureSecretRevealedNotification(run, "secret-1", "Cult", "The mayor leads the cult."));
            await n.NotifyAdventureNpcUpdatedAsync(session.Id, new AdventureNpcUpdatedNotification(run, "npc-1", "Mayor", false, true));
            await n.NotifyAdventureCompletedAsync(session.Id, new AdventureCompletedNotification(run, "DM notes: the mayor escaped."));
            await n.NotifyParticipantJoinedAsync(session.Id, new ParticipantDto(Guid.NewGuid(), session.Id, "Newcomer", ParticipantRole.Player, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            await n.NotifyParticipantDeniedAsync(session.Id, new ParticipantDeniedNotification(session.Id, Guid.NewGuid(), "Someone"));
        });

        string[] hostOnly =
        [
            "AIProposalCreated", "StateUpdated", "ImportStatusUpdated", "AdventureClueRevealed", "AdventureSecretRevealed",
            "AdventureNpcUpdated", "AdventureCompleted", "ParticipantJoined", "ParticipantDenied",
        ];
        foreach (var name in hostOnly)
            await host.WaitFor(name);

        // ...and the table's events still reach the player.
        await NotifyAsync(n => n.NotifyTurnAdvancedAsync(session.Id, new TurnAdvancedNotification(run, null, alice.ParticipantId, "Alice", 1)));
        await aliceHub.WaitFor("TurnAdvanced");
        await Task.Delay(Quiet);

        foreach (var name in hostOnly)
            Assert.False(aliceHub.Got(name), $"A player received the host-only event {name}.");
    }

    [Fact]
    public async Task PrivateRoll_ReachesOnlyTheRollerAndTheHost_PublicRollReachesEveryone()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        var bob = await JoinApprovedAsync(session, "Bob");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        await using var bobHub = await JoinedAsync(bob.ParticipantToken, session.Id, bob.ParticipantId, "Bob");

        await NotifyAsync(n => n.NotifyRollRecordedAsync(session.Id, Roll(session.Id, bob.ParticipantId, RollVisibility.Private, "secret insight")));
        await host.WaitFor("RollRecorded");
        await bobHub.WaitFor("RollRecorded");

        await NotifyAsync(n => n.NotifyRollRecordedAsync(session.Id, Roll(session.Id, bob.ParticipantId, RollVisibility.Public, "attack")));
        var seenByAlice = await aliceHub.WaitFor("RollRecorded");
        await Task.Delay(Quiet);

        Assert.Equal("attack", seenByAlice.GetProperty("roll").GetProperty("context").GetString());
        Assert.Single(aliceHub.All("RollRecorded"));   // the public one only
        Assert.Equal(2, host.All("RollRecorded").Count());
        Assert.Equal(2, bobHub.All("RollRecorded").Count());
    }

    [Fact]
    public async Task HostOnTheTrustedLocalMachine_WorksWithoutAToken()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        var hostClientId = Guid.NewGuid();

        await ChatAsync(host, session.Id, hostClientId, "Anything", "Welcome to the table.");
        var chat = await aliceHub.WaitFor("ChatMessageReceived");
        Assert.Equal("Host", chat.GetProperty("displayName").GetString());
        // The host has no participant record; its client recognises its own messages by the id it chose.
        Assert.Equal(hostClientId, chat.GetProperty("participantId").GetGuid());

        await NotifyAsync(n => n.NotifyAIProposalCreatedAsync(session.Id, new AIProposalCreatedNotification(session.Id, Guid.NewGuid(), "AdvanceScene", "Move on", "{}")));
        await host.WaitFor("AIProposalCreated");
    }

    [Fact]
    public async Task NoToken_FromTheLan_IsNotTheHost_AndCannotConnect()
    {
        await using var stranger = await ConnectAsync(token: null, from: LanAddress, start: false);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => stranger.Connection.StartAsync());

        Assert.Contains("401", refused.ToString());
    }

    [Fact]
    public async Task EndingTheSession_DisconnectsEveryone()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        var wanda = await JoinAsync(session, "Wanda");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await JoinedAsync(alice.ParticipantToken, session.Id, alice.ParticipantId, "Alice");
        await using var wandaHub = await JoinedAsync(wanda.ParticipantToken, session.Id, wanda.ParticipantId, "Wanda");

        using (var rest = Host())
        {
            (await rest.PostAsJsonAsync($"/api/sessions/{session.Id}/end", new { Summary = (string?)null })).EnsureSuccessStatusCode();
        }

        foreach (var probe in new[] { host, aliceHub, wandaHub })
            Assert.True(await Task.WhenAny(probe.Closed.Task, Task.Delay(Patience)) == probe.Closed.Task, "A connection to the ended session stayed open.");

        // Nobody gets back in: the session's tokens are revoked, and the session no longer takes joins.
        await using var again = await ConnectAsync(alice.ParticipantToken, start: false);
        await Assert.ThrowsAnyAsync<Exception>(() => again.Connection.StartAsync());
    }

    /// <summary>
    /// An automatic reconnect is a new connection. With a valid token it is back in its groups as soon as it
    /// opens (before the client calls JoinSession again).
    /// </summary>
    [Fact]
    public async Task NewConnectionWithAValidToken_IsInItsGroupsStraightAway()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await ConnectAsync(alice.ParticipantToken);   // no JoinSession call

        // The server admits the connection just after the handshake, so keep talking until it hears.
        var deadline = DateTime.UtcNow + Patience;
        while (!aliceHub.Got("ChatMessageReceived") && DateTime.UtcNow < deadline)
        {
            await ChatAsync(host, session.Id, Guid.Empty, "Host", "are you back?");
            await Task.Delay(200);
        }

        Assert.True(aliceHub.Got("ChatMessageReceived"), "A connection with a valid token was not put back in its groups.");
    }

    [Fact]
    public async Task ExpiredToken_IsRefused()
    {
        var session = await StartSessionAsync();
        var alice = await JoinApprovedAsync(session, "Alice");
        var expired = SignToken(session.Id, alice.ParticipantId, "Alice", "Player", expires: DateTime.UtcNow.AddMinutes(-10));
        await using var probe = await ConnectAsync(expired, start: false);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => probe.Connection.StartAsync());

        Assert.Contains("401", refused.ToString());
    }

    /// <summary>A participant token signed with the server's key: what the token service would issue, with a chosen expiry.</summary>
    private static string SignToken(Guid sessionId, Guid participantId, string name, string role, DateTime expires)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "aircane",
            audience: "aircane",
            claims:
            [
                new Claim("sid", sessionId.ToString()),
                new Claim("pid", participantId.ToString()),
                new Claim("dname", name),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            notBefore: expires.AddHours(-1),
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
