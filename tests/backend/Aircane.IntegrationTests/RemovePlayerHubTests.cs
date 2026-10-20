using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Removal and presence over real SignalR connections, with real participant tokens and the real notifier: who is
/// told, whose connection closes, who may come back, and who hears that a player connected or went away.
/// </summary>
public sealed class RemovePlayerHubTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] Events =
    [
        "ParticipantJoined", "ParticipantLeft", "ParticipantApproved", "ParticipantDenied", "ParticipantRemoved",
        "ParticipantPresenceChanged", "AdventureStateChanged", "TurnAdvanced", "ChatMessageReceived",
    ];

    private readonly DesktopModeWebApplicationFactory _factory;

    public RemovePlayerHubTests(DesktopModeWebApplicationFactory factory)
    {
        _factory = factory;
        _ = _factory.CreateClient();
    }

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private async Task<SessionDto> StartSessionAsync()
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Removal hub", "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        return (await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = false }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private async Task<JoinSessionResult> JoinAsync(SessionDto session, string name)
    {
        using var lan = _factory.CreateClientFrom(LanAddress);
        return (await (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = name, InviteCode = session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
    }

    private sealed class Probe : IAsyncDisposable
    {
        public HubConnection Connection { get; }
        public ConcurrentQueue<(string Event, JsonElement Payload)> Received { get; } = new();
        public TaskCompletionSource<Exception?> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _arrived = new(0);

        public Probe(HubConnection connection)
        {
            Connection = connection;
            foreach (var name in Events)
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

        public bool Got(string name, Func<JsonElement, bool>? match = null)
            => Received.Any(r => r.Event == name && (match is null || match(r.Payload)));

        public async Task<JsonElement> WaitFor(string name, Func<JsonElement, bool>? match = null)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                var hit = Received.Where(r => r.Event == name && (match is null || match(r.Payload)))
                    .Select(r => (JsonElement?)r.Payload).FirstOrDefault();
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

    private async Task<Probe> ConnectAsync(string? token)
    {
        var remoteIp = (token is null ? IPAddress.Loopback : LanAddress).ToString();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/session"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers[DesktopModeWebApplicationFactory.RemoteIpHeader] = remoteIp;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var client = _factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers[DesktopModeWebApplicationFactory.RemoteIpHeader] = remoteIp;
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
        await connection.StartAsync();
        return probe;
    }

    private async Task<Probe> HostOnHubAsync(SessionDto session)
    {
        var probe = await ConnectAsync(null);
        await probe.Connection.InvokeAsync("JoinSession", new { SessionId = session.Id, ParticipantId = Guid.NewGuid(), DisplayName = "Host", Role = 0 });
        return probe;
    }

    private static Func<JsonElement, bool> About(Guid participantId)
        => e => e.GetProperty("participantId").GetGuid() == participantId;

    [Fact]
    public async Task TheRemovedPerson_IsTold_ThenTheirConnectionCloses_AndTheyCannotComeBack()
    {
        var session = await StartSessionAsync();
        var alice = await JoinAsync(session, "Alice");
        var bob = await JoinAsync(session, "Bob");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await ConnectAsync(alice.ParticipantToken);
        await using var bobHub = await ConnectAsync(bob.ParticipantToken);

        using (var client = Host())
        {
            (await client.PostAsJsonAsync($"/api/sessions/{session.Id}/remove-participant", new { participantId = bob.ParticipantId }))
                .EnsureSuccessStatusCode();
        }

        await host.WaitFor("ParticipantRemoved", About(bob.ParticipantId));
        Assert.True(await Task.WhenAny(bobHub.Closed.Task, Task.Delay(Patience)) == bobHub.Closed.Task, "The removed player's connection stayed open.");
        Assert.True(bobHub.Got("ParticipantRemoved", About(bob.ParticipantId)), "The removed player was not told before being disconnected.");
        await aliceHub.WaitFor("ParticipantLeft", About(bob.ParticipantId));
        await Task.Delay(Quiet);
        Assert.False(aliceHub.Got("ParticipantRemoved"), "The table was told about the removal itself (it hears ParticipantLeft).");

        // The token is revoked: a new connection with it is refused outright.
        await Assert.ThrowsAnyAsync<Exception>(async () => await ConnectAsync(bob.ParticipantToken));
    }

    [Fact]
    public async Task Presence_GoesToTheHostOnly_AsAFlag_AndShowsOnTheHostsRoster()
    {
        var session = await StartSessionAsync();
        var alice = await JoinAsync(session, "Alice");
        var bob = await JoinAsync(session, "Bob");
        await using var host = await HostOnHubAsync(session);
        await using var aliceHub = await ConnectAsync(alice.ParticipantToken);

        // Bob connects: the host hears it, Alice does not.
        var bobHub = await ConnectAsync(bob.ParticipantToken);
        var connected = await host.WaitFor("ParticipantPresenceChanged", e => About(bob.ParticipantId)(e) && e.GetProperty("isConnected").GetBoolean());
        var fields = connected.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(["isConnected", "participantId", "sessionId"], fields);   // no connection id, no token

        using (var client = Host())
        {
            var roster = (await client.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{session.Id}/participants", Web))!;
            Assert.True(roster.Single(p => p.Id == bob.ParticipantId).IsConnected);
            Assert.True(roster.Single(p => p.Id == alice.ParticipantId).IsConnected);
        }

        // Bob goes away: the host hears that too, and nothing changes for anyone else.
        await bobHub.DisposeAsync();
        await host.WaitFor("ParticipantPresenceChanged", e => About(bob.ParticipantId)(e) && !e.GetProperty("isConnected").GetBoolean());
        using (var client = Host())
        {
            var roster = (await client.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{session.Id}/participants", Web))!;
            Assert.False(roster.Single(p => p.Id == bob.ParticipantId).IsConnected);
            Assert.True(roster.Single(p => p.Id == bob.ParticipantId).IsApproved);   // away is not removed
        }

        await Task.Delay(Quiet);
        Assert.False(aliceHub.Got("ParticipantPresenceChanged"), "A player heard another player's presence.");
    }
}
