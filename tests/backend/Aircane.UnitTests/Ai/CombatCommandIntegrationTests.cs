using System.Text.Json;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Domain.Combat;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Ai;

/// <summary>
/// End-to-end tests for combat commands flowing through the real
/// <see cref="StateCommandExecutor"/> and <see cref="CampaignStateService"/> into the encounter
/// stored in campaign state JSON. Runs at FullSessionControl so commands auto-apply.
/// Verifies that damage/healing/conditions — which previously fell through to a no-op generic
/// merge — now actually mutate the encounter, and that initiative/turn/death-save work.
/// </summary>
public class CombatCommandIntegrationTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly CampaignStateService _stateService;
    private readonly StateCommandExecutor _sut;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public CombatCommandIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _stateService = new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance);
        var proposalService = new AiProposalService(_db, _stateService, NullLogger<AiProposalService>.Instance);

        var validators = new IStateCommandValidator[]
        {
            new ApplyDamageValidator(),
            new ApplyHealingValidator(),
            new ApplyConditionValidator(),
            new RemoveConditionValidator(),
        };

        _sut = new StateCommandExecutor(
            validators,
            new AiRoleConfigurationService(),
            new AiAuthorityConfigurationService(),
            _stateService,
            proposalService,
            NullLogger<StateCommandExecutor>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(Campaign Campaign, Session Session, StateCommandContext Context)> SeedAsync()
    {
        var campaign = new Campaign("Combat", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(campaign);
        var session = new Session(campaign.Id, "S", SessionAccessMode.LocalLan, "hash");
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();
        return (campaign, session, new StateCommandContext(campaign.Id, session.Id, campaign.AiRole, campaign.AiAuthority));
    }

    private async Task<EncounterState?> LoadEncounterAsync(Guid campaignId)
    {
        var state = await _stateService.LoadStateAsync(campaignId);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(state.StateJson)!;
        if (!dict.TryGetValue("encounter", out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return el.Deserialize<EncounterState>(JsonOptions);
    }

    private static string StartEncounterPayload(params (string id, string name, bool pc, int hp)[] combatants)
    {
        var arr = combatants.Select(c => new
        {
            id = c.id,
            name = c.name,
            isPlayerCharacter = c.pc,
            currentHp = c.hp,
            maxHp = c.hp,
        });
        return JsonSerializer.Serialize(new { combatants = arr }, JsonOptions);
    }

    // ── Encounter lifecycle ────────────────────────────────────────────────────

    [Fact]
    public async Task StartEncounter_ThenRollInitiative_EstablishesTurnOrder()
    {
        var (campaign, _, ctx) = await SeedAsync();

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.StartEncounter,
            CombatPayloadJson = StartEncounterPayload(("hero", "Hero", true, 20), ("goblin", "Goblin", false, 7)),
        }, ctx);

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.RollInitiative,
            CombatPayloadJson = JsonSerializer.Serialize(new { initiative = new { hero = 18, goblin = 12 } }),
        }, ctx);

        var enc = await LoadEncounterAsync(campaign.Id);
        Assert.NotNull(enc);
        Assert.True(enc!.IsActive);
        Assert.Equal(1, enc.Round);
        Assert.Equal(["hero", "goblin"], enc.InitiativeOrder);
        Assert.Equal("hero", enc.ActiveCombatant!.Id);
    }

    [Fact]
    public async Task AdvanceTurn_MovesThroughInitiative()
    {
        var (campaign, _, ctx) = await SeedAsync();
        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.StartEncounter,
            CombatPayloadJson = StartEncounterPayload(("a", "A", true, 20), ("b", "B", false, 7)),
        }, ctx);
        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.RollInitiative,
            CombatPayloadJson = JsonSerializer.Serialize(new { initiative = new { a = 20, b = 10 } }),
        }, ctx);

        await _sut.ExecuteAsync(new AiProposedAction { Type = AiActionType.AdvanceTurn }, ctx);

        var enc = await LoadEncounterAsync(campaign.Id);
        Assert.Equal("b", enc!.ActiveCombatant!.Id);
    }

    // ── Damage / healing actually mutate the encounter (previously no-op) ───────

    [Fact]
    public async Task ApplyDamage_ToUnknownCombatant_IsNoOp()
    {
        // Damage whose target id matches no combatant leaves the encounter unchanged.
        var (campaign, _, ctx) = await SeedAsync();
        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.StartEncounter,
            CombatPayloadJson = StartEncounterPayload(("goblin", "Goblin", false, 7)),
        }, ctx);

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = Guid.NewGuid(), // does not match combatant id "goblin"
            Amount = 3,
        }, ctx);

        var enc = await LoadEncounterAsync(campaign.Id);
        Assert.Equal(7, enc!.Combatants.First(c => c.Id == "goblin").CurrentHp);
    }

    [Fact]
    public async Task ApplyDamage_And_Healing_OnPlayerCharacter_ByGuidId()
    {
        var (campaign, _, ctx) = await SeedAsync();
        var pcId = Guid.NewGuid();

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.StartEncounter,
            CombatPayloadJson = StartEncounterPayload((pcId.ToString(), "Hero", true, 20)),
        }, ctx);

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.ApplyDamage,
            CharacterId = pcId,
            Amount = 8,
        }, ctx);

        var afterDamage = await LoadEncounterAsync(campaign.Id);
        Assert.Equal(12, afterDamage!.Combatants[0].CurrentHp);

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.ApplyHealing,
            CharacterId = pcId,
            Amount = 100,
        }, ctx);

        var afterHeal = await LoadEncounterAsync(campaign.Id);
        Assert.Equal(20, afterHeal!.Combatants[0].CurrentHp); // capped at max
    }

    // ── Conditions with duration ───────────────────────────────────────────────

    [Fact]
    public async Task ApplyCondition_WithDuration_ThenRemove()
    {
        var (campaign, _, ctx) = await SeedAsync();
        var pcId = Guid.NewGuid();
        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.StartEncounter,
            CombatPayloadJson = StartEncounterPayload((pcId.ToString(), "Hero", true, 20)),
        }, ctx);

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.ApplyCondition,
            CharacterId = pcId,
            ConditionName = "Poisoned",
            RemainingRounds = 3,
        }, ctx);

        var withCond = await LoadEncounterAsync(campaign.Id);
        var cond = Assert.Single(withCond!.Combatants[0].Conditions);
        Assert.Equal("Poisoned", cond.Name);
        Assert.Equal(3, cond.RemainingRounds);

        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.RemoveCondition,
            CharacterId = pcId,
            ConditionName = "Poisoned",
        }, ctx);

        var removed = await LoadEncounterAsync(campaign.Id);
        Assert.Empty(removed!.Combatants[0].Conditions);
    }

    // ── Death saves ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownedPc_DeathSaves_AccumulateToDeath()
    {
        var (campaign, _, ctx) = await SeedAsync();
        var pcId = Guid.NewGuid();
        await _sut.ExecuteAsync(new AiProposedAction
        {
            Type = AiActionType.StartEncounter,
            CombatPayloadJson = StartEncounterPayload((pcId.ToString(), "Hero", true, 5)),
        }, ctx);

        await _sut.ExecuteAsync(new AiProposedAction { Type = AiActionType.ApplyDamage, CharacterId = pcId, Amount = 10 }, ctx);

        for (var i = 0; i < 3; i++)
        {
            await _sut.ExecuteAsync(new AiProposedAction
            {
                Type = AiActionType.DeathSave,
                CharacterId = pcId,
                Success = false,
            }, ctx);
        }

        var enc = await LoadEncounterAsync(campaign.Id);
        var pc = enc!.Combatants[0];
        Assert.Equal(0, pc.CurrentHp);
        Assert.Equal(3, pc.DeathSaves!.Failures);
        Assert.True(pc.IsDead);
    }
}
