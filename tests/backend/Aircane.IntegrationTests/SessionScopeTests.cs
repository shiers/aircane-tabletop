using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Session scope on the REST API, through the token-validating desktop-mode factory: a token is good for
/// its own session only, a participant must be approved and not denied, and what a Player reads in their
/// own session is filtered for a Player (private rolls, NPC sheets, NPC hit points).
/// <para>
/// Two tables, A and B, in different campaigns. "Alice" is an approved Player of A; "Bob" and "Cara" are
/// approved Players of B; "Dana" is a HumanDm of A. Joining always makes a Player, so the HumanDm token
/// is issued by the token service directly, as the app would for a participant with that role.
/// </para>
/// </summary>
public sealed class SessionScopeTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly DesktopModeWebApplicationFactory _factory;

    public SessionScopeTests(DesktopModeWebApplicationFactory factory) => _factory = factory;

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

    private async Task<Table> StartTableAsync(string name)
    {
        using var host = Host();
        var campaign = await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest(name, "D&D 5e 2014", "PHB"))).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<CampaignDto>();
        var session = await (await host.PostAsJsonAsync($"/api/campaigns/{campaign!.Id}/start-session",
            new { Name = name, AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = true }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>();
        return new Table(campaign.Id, session!);
    }

    private async Task<Seat> JoinAsync(Table table, string name, bool approve = true)
    {
        using var lan = _factory.CreateClientFrom(LanAddress);
        var joined = (await (await lan.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/join",
            new { DisplayName = name, InviteCode = table.Session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
        if (approve)
        {
            using var host = Host();
            (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/approve-participant",
                new { ParticipantId = joined.ParticipantId })).EnsureSuccessStatusCode();
        }
        return new Seat(joined.ParticipantId, joined.ParticipantToken);
    }

    /// <summary>An approved HumanDm participant of the table, with a token the app's token service signed.</summary>
    private async Task<Seat> HumanDmAsync(Table table, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var participant = new SessionParticipant(table.Session.Id, name, ParticipantRole.HumanDm, isApproved: true);
        db.SessionParticipants.Add(participant);
        await db.SaveChangesAsync();
        var issued = scope.ServiceProvider.GetRequiredService<IParticipantTokenService>()
            .IssueToken(table.Session.Id, participant.Id, name, nameof(ParticipantRole.HumanDm));
        return new Seat(participant.Id, issued.Token);
    }

    private async Task<Guid> SeedCharacterAsync(Table table, string name, CharacterRole role, Guid? ownerParticipantId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var character = new Character(name, "D&D 5e 2014", "PHB", 3, "{}", "{}", table.CampaignId, ownerParticipantId, role);
        db.Characters.Add(character);
        await db.SaveChangesAsync();
        return character.Id;
    }

    private async Task StartEncounterAsync(Table table)
    {
        using var host = Host();
        (await host.PostAsJsonAsync($"/api/sessions/{table.Session.Id}/combat/start", new
        {
            combatants = new object[]
            {
                new { id = "pc-1", name = "Bob's fighter", isPlayerCharacter = true, currentHp = 24, maxHp = 30, initiative = 15 },
                new { id = "npc-1", name = "Ogre", isPlayerCharacter = false, currentHp = 59, maxHp = 59, initiative = 8 },
            },
        })).EnsureSuccessStatusCode();
    }

    private static object RollBody(Guid roller, RollVisibility visibility = RollVisibility.Public, string context = "Perception")
        => new { rollerParticipantId = roller, formula = "1d20", visibility = (int)visibility, context };

    private static object ManualRollBody(Guid roller)
        => new { rollerParticipantId = roller, dieResults = new[] { 12 }, modifier = 2, total = 14, formula = "1d20+2" };

    // ═════════════════════════════════════════════════════════════════════════
    // Step 1: a token from session A against session B
    // ═════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> CrossSessionPlayerCalls => new()
    {
        "POST rolls", "POST manual-rolls", "GET rolls", "GET session", "GET participants",
    };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string call, Guid sessionId, Guid roller) => call switch
    {
        "POST rolls" => client.PostAsJsonAsync($"/api/sessions/{sessionId}/rolls", RollBody(roller)),
        "POST manual-rolls" => client.PostAsJsonAsync($"/api/sessions/{sessionId}/manual-rolls", ManualRollBody(roller)),
        "GET rolls" => client.GetAsync($"/api/sessions/{sessionId}/rolls"),
        "GET session" => client.GetAsync($"/api/sessions/{sessionId}"),
        "GET participants" => client.GetAsync($"/api/sessions/{sessionId}/participants"),
        "GET proposals" => client.GetAsync($"/api/sessions/{sessionId}/ai/proposals"),
        "GET combat" => client.GetAsync($"/api/sessions/{sessionId}/combat"),
        _ => throw new ArgumentOutOfRangeException(nameof(call)),
    };

    [Theory]
    [MemberData(nameof(CrossSessionPlayerCalls))]
    public async Task PlayerTokenOfSessionA_IsRefusedOnSessionB(string call)
    {
        var a = await StartTableAsync("A");
        var b = await StartTableAsync("B");
        var alice = await JoinAsync(a, "Alice");
        var bob = await JoinAsync(b, "Bob");
        using var client = As(alice.Token);

        using var response = await SendAsync(client, call, b.Session.Id, bob.ParticipantId);

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{call} on another session with a Player token returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    public static TheoryData<string> CrossSessionDmCalls => new() { "GET proposals", "GET combat", "GET rolls", "GET participants" };

    [Theory]
    [MemberData(nameof(CrossSessionDmCalls))]
    public async Task HumanDmTokenOfSessionA_IsRefusedOnSessionB(string call)
    {
        var a = await StartTableAsync("A");
        var b = await StartTableAsync("B");
        var dana = await HumanDmAsync(a, "Dana");
        await StartEncounterAsync(b);
        using var client = As(dana.Token);

        using var response = await SendAsync(client, call, b.Session.Id, Guid.NewGuid());

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{call} on another session with a HumanDm token returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    /// <summary>
    /// Campaign- and character-keyed reads. A HumanDm passes the DmOrHost role check, which says nothing
    /// about which campaign, so another table's campaign, state and character sheets were readable.
    /// </summary>
    [Theory]
    [InlineData("GET character")]
    [InlineData("GET campaign")]
    [InlineData("GET campaign state")]
    [InlineData("GET campaign characters")]
    public async Task HumanDmTokenOfSessionA_IsRefusedOnSessionBsCampaignAndCharacters(string call)
    {
        var a = await StartTableAsync("A");
        var b = await StartTableAsync("B");
        var dana = await HumanDmAsync(a, "Dana");
        var ogre = await SeedCharacterAsync(b, "The Ogre Chief", CharacterRole.Npc);
        using var client = As(dana.Token);

        using var response = call switch
        {
            "GET character" => await client.GetAsync($"/api/characters/{ogre}"),
            "GET campaign" => await client.GetAsync($"/api/campaigns/{b.CampaignId}"),
            "GET campaign state" => await client.GetAsync($"/api/campaigns/{b.CampaignId}/state"),
            _ => await client.GetAsync($"/api/characters?campaignId={b.CampaignId}"),
        };

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{call} of another table with a HumanDm token returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task HumanDm_ListingAllCharacters_GetsOnlyTheirOwnCampaigns()
    {
        var a = await StartTableAsync("A");
        var b = await StartTableAsync("B");
        var dana = await HumanDmAsync(a, "Dana");
        await SeedCharacterAsync(a, "A's villain", CharacterRole.Npc);
        await SeedCharacterAsync(b, "B's villain", CharacterRole.Npc);
        using var client = As(dana.Token);

        var body = await client.GetStringAsync("/api/characters");

        Assert.Contains("A's villain", body);
        Assert.DoesNotContain("B's villain", body);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Step 1: a Player in their own session
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Player_DoesNotSeeSomeoneElsesPrivateRolls_InTheirOwnSessionsLog()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var cara = await JoinAsync(b, "Cara");
        using (var asBob = As(bob.Token))
        {
            (await asBob.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/rolls", RollBody(bob.ParticipantId, RollVisibility.Private, "secret insight")))
                .EnsureSuccessStatusCode();
            (await asBob.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/rolls", RollBody(bob.ParticipantId, RollVisibility.Public, "attack")))
                .EnsureSuccessStatusCode();
        }
        using var asCara = As(cara.Token);

        var log = await asCara.GetStringAsync($"/api/sessions/{b.Session.Id}/rolls");

        Assert.Contains("attack", log);
        Assert.DoesNotContain("secret insight", log);
    }

    [Fact]
    public async Task Player_RollingWithSomeoneElsesId_IsRecordedAsThemselves()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var cara = await JoinAsync(b, "Cara");
        using var asBob = As(bob.Token);

        using var response = await asBob.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/rolls", RollBody(cara.ParticipantId));

        response.EnsureSuccessStatusCode();
        var roll = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(bob.ParticipantId, roll.GetProperty("rollerParticipantId").GetGuid());
    }

    [Fact]
    public async Task Player_ReadsCombat_WithoutNpcHitPoints()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        await StartEncounterAsync(b);
        using var asBob = As(bob.Token);

        using var response = await asBob.GetAsync($"/api/sessions/{b.Session.Id}/combat/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ogre = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("combatants").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "Ogre");
        Assert.Equal(JsonValueKind.Null, ogre.GetProperty("currentHp").ValueKind);
        Assert.Equal(JsonValueKind.Null, ogre.GetProperty("maxHp").ValueKind);
    }

    [Fact]
    public async Task Player_ListsOnlyTheirOwnCharacters_AndNoNpcs()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        await SeedCharacterAsync(b, "Bob's fighter", CharacterRole.Player, bob.ParticipantId);
        await SeedCharacterAsync(b, "Cara's wizard", CharacterRole.Player);
        await SeedCharacterAsync(b, "The Ogre Chief", CharacterRole.Npc);
        using var asBob = As(bob.Token);

        using var response = await asBob.GetAsync($"/api/characters?campaignId={b.CampaignId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bob's fighter", body);
        Assert.DoesNotContain("Cara's wizard", body);
        Assert.DoesNotContain("The Ogre Chief", body);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Step 3: the same session works, and reads are role-aware
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CrossSessionPlayerCalls))]
    public async Task PlayerToken_WorksOnItsOwnSession(string call)
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        using var client = As(bob.Token);

        using var response = await SendAsync(client, call, b.Session.Id, bob.ParticipantId);

        Assert.True(response.IsSuccessStatusCode, $"{call} on the player's own session returned {(int)response.StatusCode}.");
    }

    [Theory]
    [MemberData(nameof(CrossSessionDmCalls))]
    public async Task HumanDmToken_WorksOnItsOwnSession(string call)
    {
        var a = await StartTableAsync("A");
        var dana = await HumanDmAsync(a, "Dana");
        await StartEncounterAsync(a);
        using var client = As(dana.Token);

        using var response = await SendAsync(client, call, a.Session.Id, Guid.NewGuid());

        Assert.True(response.IsSuccessStatusCode, $"{call} on the DM's own session returned {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task HumanDm_GetsTheFullEncounter_WithNpcHitPoints_InTheirOwnSession()
    {
        var a = await StartTableAsync("A");
        var dana = await HumanDmAsync(a, "Dana");
        await StartEncounterAsync(a);
        using var client = As(dana.Token);

        var body = await client.GetStringAsync($"/api/sessions/{a.Session.Id}/combat");

        Assert.Contains("\"maxHp\":59", body);
    }

    [Fact]
    public async Task Player_CannotReadTheFullEncounter_AndThePublicOneHasNoNpcHitPoints()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        await StartEncounterAsync(b);
        using var client = As(bob.Token);

        using var full = await client.GetAsync($"/api/sessions/{b.Session.Id}/combat");
        Assert.Equal(HttpStatusCode.Forbidden, full.StatusCode);

        var view = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{b.Session.Id}/combat/public");
        var combatants = view.GetProperty("combatants").EnumerateArray().ToList();
        var ogre = combatants.Single(c => c.GetProperty("name").GetString() == "Ogre");
        var fighter = combatants.Single(c => c.GetProperty("isPlayerCharacter").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ogre.GetProperty("currentHp").ValueKind);
        Assert.Equal(JsonValueKind.Null, ogre.GetProperty("maxHp").ValueKind);
        Assert.Equal(JsonValueKind.Null, ogre.GetProperty("temporaryHp").ValueKind);
        Assert.Equal(JsonValueKind.Null, ogre.GetProperty("deathSaves").ValueKind);
        Assert.Equal(24, fighter.GetProperty("currentHp").GetInt32());   // the party's own state is public
        Assert.Equal(30, fighter.GetProperty("maxHp").GetInt32());
        Assert.Equal(8, ogre.GetProperty("initiative").GetInt32());
    }

    [Fact]
    public async Task PublicEncounter_WithNoEncounter_Is204()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        using var client = As(bob.Token);

        using var response = await client.GetAsync($"/api/sessions/{b.Session.Id}/combat/public");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Player_CannotReadAnNpcSheet_OrSomeoneElsesCharacter_ById()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var mine = await SeedCharacterAsync(b, "Bob's fighter", CharacterRole.Player, bob.ParticipantId);
        var npc = await SeedCharacterAsync(b, "The Ogre Chief", CharacterRole.Npc);
        var theirs = await SeedCharacterAsync(b, "Cara's wizard", CharacterRole.Player);
        using var client = As(bob.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/characters/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/characters/{npc}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/characters/{theirs}")).StatusCode);
    }

    [Fact]
    public async Task Player_SeesTheCharacterTheHostAssignedThem()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var assigned = await SeedCharacterAsync(b, "Pregenerated cleric", CharacterRole.Player);
        using (var host = Host())
        {
            (await host.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/assign-character",
                new { ParticipantId = bob.ParticipantId, CharacterId = assigned })).EnsureSuccessStatusCode();
        }
        using var client = As(bob.Token);

        var body = await client.GetStringAsync("/api/characters");

        Assert.Contains("Pregenerated cleric", body);
    }

    [Fact]
    public async Task Player_WithNoCharacters_GetsAnEmptyList()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        await SeedCharacterAsync(b, "The Ogre Chief", CharacterRole.Npc);
        using var client = As(bob.Token);

        using var response = await client.GetAsync("/api/characters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task Player_CannotWriteCharacters()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var mine = await SeedCharacterAsync(b, "Bob's fighter", CharacterRole.Player, bob.ParticipantId);
        using var client = As(bob.Token);

        using var delete = await client.DeleteAsync($"/api/characters/{mine}");

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Roller_SeesTheirOwnPrivateRoll_AndTheHostSeesAll()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        using (var asBob = As(bob.Token))
        {
            (await asBob.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/rolls", RollBody(bob.ParticipantId, RollVisibility.Private, "secret insight")))
                .EnsureSuccessStatusCode();
            Assert.Contains("secret insight", await asBob.GetStringAsync($"/api/sessions/{b.Session.Id}/rolls"));
        }

        using var host = Host();
        var hostLog = await host.GetStringAsync($"/api/sessions/{b.Session.Id}/rolls");
        Assert.Contains("secret insight", hostLog);
        Assert.Contains("\"rollerName\":\"Bob\"", hostLog);
    }

    [Fact]
    public async Task HumanDm_SeesPrivateRolls_InTheirOwnSession()
    {
        var a = await StartTableAsync("A");
        var alice = await JoinAsync(a, "Alice");
        var dana = await HumanDmAsync(a, "Dana");
        using (var asAlice = As(alice.Token))
        {
            (await asAlice.PostAsJsonAsync($"/api/sessions/{a.Session.Id}/manual-rolls", new
            {
                rollerParticipantId = alice.ParticipantId, dieResults = new[] { 3 }, modifier = 0, total = 3,
                formula = "1d20", visibility = (int)RollVisibility.Private, context = "sneaking",
            })).EnsureSuccessStatusCode();
        }
        using var client = As(dana.Token);

        Assert.Contains("sneaking", await client.GetStringAsync($"/api/sessions/{a.Session.Id}/rolls"));
    }

    [Fact]
    public async Task ManualRoll_WithSomeoneElsesId_IsRecordedAsTheRoller()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var cara = await JoinAsync(b, "Cara");
        using var asBob = As(bob.Token);

        using var response = await asBob.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/manual-rolls", ManualRollBody(cara.ParticipantId));

        response.EnsureSuccessStatusCode();
        Assert.Equal(bob.ParticipantId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rollerParticipantId").GetGuid());
    }

    [Fact]
    public async Task HostOnTheTrustedLocalMachine_MayRollForAPlayer()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        using var host = Host();

        using var response = await host.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/rolls", RollBody(bob.ParticipantId));

        response.EnsureSuccessStatusCode();
        Assert.Equal(bob.ParticipantId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rollerParticipantId").GetGuid());
    }

    [Fact]
    public async Task HostOnTheTrustedLocalMachine_IsUnchanged()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var wanda = await JoinAsync(b, "Wanda", approve: false);
        await SeedCharacterAsync(b, "The Ogre Chief", CharacterRole.Npc);
        await StartEncounterAsync(b);
        using var host = Host();

        Assert.Contains("\"maxHp\":59", await host.GetStringAsync($"/api/sessions/{b.Session.Id}/combat"));
        Assert.Contains("The Ogre Chief", await host.GetStringAsync("/api/characters"));
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync($"/api/sessions/{b.Session.Id}/ai/proposals")).StatusCode);
        var roster = await host.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{b.Session.Id}/participants", Web);
        Assert.NotEqual(default, roster!.Single(p => p.Id == wanda.ParticipantId).JoinedAt);   // the host sees every detail
        Assert.Contains(roster, p => p.Id == bob.ParticipantId);
    }

    [Fact]
    public async Task Player_SeesWaitingParticipantsByNameOnly()
    {
        var b = await StartTableAsync("B");
        var bob = await JoinAsync(b, "Bob");
        var wanda = await JoinAsync(b, "Wanda", approve: false);
        using var client = As(bob.Token);

        var roster = await client.GetFromJsonAsync<List<ParticipantDto>>($"/api/sessions/{b.Session.Id}/participants", Web);

        var waiting = roster!.Single(p => p.Id == wanda.ParticipantId);
        Assert.Equal("Wanda", waiting.DisplayName);
        Assert.False(waiting.IsApproved);
        Assert.Equal(default, waiting.JoinedAt);
        Assert.Equal(default, waiting.LastSeenAt);
        Assert.Null(waiting.CharacterId);
        Assert.NotEqual(default, roster!.Single(p => p.Id == bob.ParticipantId).JoinedAt);
    }

    [Fact]
    public async Task WaitingParticipant_GetsOnlyReconnect_AndA403WithTheReasonForEverythingElse()
    {
        var b = await StartTableAsync("B");
        var wanda = await JoinAsync(b, "Wanda", approve: false);
        using var client = As(wanda.Token);

        using var reconnect = await client.PostAsync($"/api/sessions/{b.Session.Id}/reconnect", null);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);

        foreach (var url in new[] { $"/api/sessions/{b.Session.Id}", $"/api/sessions/{b.Session.Id}/participants", $"/api/sessions/{b.Session.Id}/rolls" })
        {
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Waiting for approval", problem.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task DeniedParticipant_IsRefusedWithTheParticipantDeniedCode()
    {
        var b = await StartTableAsync("B");
        var mallory = await JoinAsync(b, "Mallory", approve: false);
        using (var host = Host())
        {
            (await host.PostAsJsonAsync($"/api/sessions/{b.Session.Id}/deny-participant",
                new { ParticipantId = mallory.ParticipantId })).EnsureSuccessStatusCode();
        }
        using var client = As(mallory.Token);

        using var response = await client.GetAsync($"/api/sessions/{b.Session.Id}/rolls");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("participant-denied", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task HumanDm_CannotApproveAnotherSessionsProposal_ThroughTheirOwnSessionsRoute()
    {
        var a = await StartTableAsync("A");
        var b = await StartTableAsync("B");
        var dana = await HumanDmAsync(a, "Dana");
        Guid proposalOfB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
            var proposal = new AiActionProposal(b.Session.Id, b.CampaignId, "RevealSecret", "{}", "Reveal the cult");
            db.AiActionProposals.Add(proposal);
            await db.SaveChangesAsync();
            proposalOfB = proposal.Id;
        }
        using var client = As(dana.Token);

        using var response = await client.PostAsJsonAsync($"/api/sessions/{a.Session.Id}/ai/proposals/{proposalOfB}/approve", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope2 = _factory.Services.CreateScope();
        var saved = await scope2.ServiceProvider.GetRequiredService<AircaneDbContext>().AiActionProposals.AsNoTracking().SingleAsync(p => p.Id == proposalOfB);
        Assert.Equal(AiProposalStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task UnknownIds_StillAnswer404_NotA403()
    {
        var b = await StartTableAsync("B");
        var dana = await HumanDmAsync(b, "Dana");
        using var client = As(dana.Token);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/characters/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/adventure-sessions/{Guid.NewGuid()}")).StatusCode);
    }
}
