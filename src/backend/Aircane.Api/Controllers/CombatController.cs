using System.Text.Json;
using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.CampaignState;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Combat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Host/DM-facing combat control endpoints. These apply combat commands (advance turn, apply
/// damage/healing, apply/remove condition, roll initiative, end encounter) directly to the
/// campaign state, since the host is the combat authority. AI-initiated combat still flows
/// through the AI-proposes → authority → approval pipeline; these endpoints are the host's direct
/// bookkeeping controls surfaced by the combat tracker UI.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId:guid}/combat")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class CombatController : ControllerBase
{
    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ICampaignStateService _stateService;
    private readonly ISessionHostingService _sessionHostingService;
    private readonly ILogger<CombatController> _logger;

    public CombatController(
        ICampaignStateService stateService,
        ISessionHostingService sessionHostingService,
        ILogger<CombatController> logger)
    {
        _stateService = stateService;
        _sessionHostingService = sessionHostingService;
        _logger = logger;
    }

    /// <summary>Returns the current live encounter for the session, or 204 if none is active.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEncounter(Guid sessionId, CancellationToken cancellationToken)
    {
        var campaignId = await ResolveCampaignIdAsync(sessionId, cancellationToken);
        if (campaignId is null)
            return NotFound();

        var state = await _stateService.LoadStateAsync(campaignId.Value, cancellationToken);
        var encounter = ReadEncounter(state.StateJson);
        if (encounter is null)
            return NoContent();

        return Ok(ToDto(encounter));
    }

    /// <summary>Starts a new encounter with the supplied combatants (initiative not yet rolled).</summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> StartEncounter(
        Guid sessionId,
        [FromBody] StartEncounterBody body,
        CancellationToken ct)
    {
        if (body.Combatants is null or { Count: 0 })
            return Task.FromResult<IActionResult>(BadRequest(Problem("At least one combatant is required.")));

        // Map the lightweight input to the domain combatant shape the engine expects.
        var combatants = body.Combatants.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            isPlayerCharacter = c.IsPlayerCharacter,
            currentHp = c.CurrentHp,
            maxHp = c.MaxHp,
            temporaryHp = c.TemporaryHp,
            initiative = c.Initiative,
        }).ToList();

        return ApplyCombatCommandAsync(sessionId, "StartEncounter", new { combatants }, ct);
    }

    /// <summary>Advances combat to the next combatant in initiative order.</summary>
    [HttpPost("advance-turn")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> AdvanceTurn(Guid sessionId, CancellationToken ct)
        => ApplyCombatCommandAsync(sessionId, "AdvanceTurn", new { }, ct);

    /// <summary>Rolls initiative for the encounter using the supplied per-combatant values.</summary>
    [HttpPost("roll-initiative")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RollInitiative(
        Guid sessionId,
        [FromBody] RollInitiativeBody body,
        CancellationToken ct)
        => ApplyCombatCommandAsync(sessionId, "RollInitiative", new { initiative = body.Initiative }, ct);

    /// <summary>Applies damage to a combatant.</summary>
    [HttpPost("apply-damage")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ApplyDamage(
        Guid sessionId,
        [FromBody] TargetAmountBody body,
        CancellationToken ct)
        => body.Amount < 0
            ? Task.FromResult<IActionResult>(BadRequestAmount())
            : ApplyCombatCommandAsync(sessionId, "ApplyDamage", new { combatantId = body.TargetId, amount = body.Amount }, ct);

    /// <summary>Applies healing to a combatant.</summary>
    [HttpPost("apply-healing")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ApplyHealing(
        Guid sessionId,
        [FromBody] TargetAmountBody body,
        CancellationToken ct)
        => body.Amount < 0
            ? Task.FromResult<IActionResult>(BadRequestAmount())
            : ApplyCombatCommandAsync(sessionId, "ApplyHealing", new { combatantId = body.TargetId, amount = body.Amount }, ct);

    /// <summary>Applies a condition to a combatant.</summary>
    [HttpPost("apply-condition")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ApplyCondition(
        Guid sessionId,
        [FromBody] ConditionBody body,
        CancellationToken ct)
        => string.IsNullOrWhiteSpace(body.ConditionName)
            ? Task.FromResult<IActionResult>(BadRequest(Problem("conditionName is required.")))
            : ApplyCombatCommandAsync(sessionId, "ApplyCondition",
                new { combatantId = body.TargetId, conditionName = body.ConditionName, remainingRounds = body.RemainingRounds }, ct);

    /// <summary>Removes a condition from a combatant.</summary>
    [HttpPost("remove-condition")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RemoveCondition(
        Guid sessionId,
        [FromBody] ConditionBody body,
        CancellationToken ct)
        => string.IsNullOrWhiteSpace(body.ConditionName)
            ? Task.FromResult<IActionResult>(BadRequest(Problem("conditionName is required.")))
            : ApplyCombatCommandAsync(sessionId, "RemoveCondition",
                new { combatantId = body.TargetId, conditionName = body.ConditionName }, ct);

    /// <summary>Records a death-saving throw for a downed combatant.</summary>
    [HttpPost("death-save")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeathSave(
        Guid sessionId,
        [FromBody] DeathSaveBody body,
        CancellationToken ct)
        => ApplyCombatCommandAsync(sessionId, "DeathSave",
            new { combatantId = body.TargetId, success = body.Success, recoversHp = body.RecoversHp }, ct);

    /// <summary>Applies the PF2e elite template to a combatant (scales it up).</summary>
    [HttpPost("apply-elite")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ApplyEliteTemplate(
        Guid sessionId,
        [FromBody] TargetBody body,
        CancellationToken ct)
        => ApplyCombatCommandAsync(sessionId, "ApplyEliteTemplate", new { combatantId = body.TargetId }, ct);

    /// <summary>Applies the PF2e weak template to a combatant (scales it down).</summary>
    [HttpPost("apply-weak")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ApplyWeakTemplate(
        Guid sessionId,
        [FromBody] TargetBody body,
        CancellationToken ct)
        => ApplyCombatCommandAsync(sessionId, "ApplyWeakTemplate", new { combatantId = body.TargetId }, ct);

    /// <summary>Ends the active encounter.</summary>
    [HttpPost("end")]
    [ProducesResponseType(typeof(EncounterStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> EndEncounter(Guid sessionId, CancellationToken ct)
        => ApplyCombatCommandAsync(sessionId, "EndEncounter", new { }, ct);

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<IActionResult> ApplyCombatCommandAsync(
        Guid sessionId,
        string commandType,
        object payload,
        CancellationToken cancellationToken)
    {
        var campaignId = await ResolveCampaignIdAsync(sessionId, cancellationToken);
        if (campaignId is null)
            return NotFound();

        var request = new ApplyCommandRequest(
            CampaignId: campaignId.Value,
            CommandType: commandType,
            PayloadJson: JsonSerializer.Serialize(payload),
            ActorType: "Host",
            ActorId: null,
            SessionId: sessionId);

        var updated = await _stateService.ApplyCommandAsync(request, cancellationToken);

        var encounter = ReadEncounter(updated.StateJson);
        return encounter is null ? NoContent() : Ok(ToDto(encounter));
    }

    private async Task<Guid?> ResolveCampaignIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _sessionHostingService.GetSessionAsync(sessionId, cancellationToken);
        return session?.CampaignId;
    }

    private BadRequestObjectResult BadRequestAmount() =>
        BadRequest(Problem("amount must be zero or positive."));

    private static ProblemDetails Problem(string detail) => new()
    {
        Title = "Invalid combat command",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest,
    };

    private static EncounterState? ReadEncounter(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(stateJson);
            if (!doc.RootElement.TryGetProperty("encounter", out var el) ||
                el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }
            return el.Deserialize<EncounterState>(CamelCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EncounterStateDto ToDto(EncounterState e) => new(
        EncounterId: e.EncounterId,
        IsActive: e.IsActive,
        Round: e.Round,
        TurnIndex: e.TurnIndex,
        ActiveCombatantId: e.ActiveCombatant?.Id,
        InitiativeOrder: e.InitiativeOrder,
        Combatants: e.Combatants.Select(c => new CombatantDto(
            Id: c.Id,
            Name: c.Name,
            IsPlayerCharacter: c.IsPlayerCharacter,
            CurrentHp: c.CurrentHp,
            MaxHp: c.MaxHp,
            TemporaryHp: c.TemporaryHp,
            Initiative: c.Initiative,
            IsDowned: c.IsDowned,
            IsDead: c.IsDead,
            Conditions: c.Conditions.Select(ci => new ConditionInstanceDto(
                ci.Name, ci.RemainingRounds, ci.AppliedOnRound, ci.EndCondition)).ToList(),
            DeathSaves: c.DeathSaves is null ? null : new DeathSaveStateDto(
                c.DeathSaves.Successes, c.DeathSaves.Failures, c.DeathSaves.IsStable, c.DeathSaves.IsDead)))
            .ToList());
}

/// <summary>Body for starting an encounter with a set of combatants.</summary>
public sealed record StartEncounterBody(IReadOnlyList<StartCombatantInput> Combatants);

/// <summary>Lightweight combatant input for starting an encounter (no conditions/death-saves).</summary>
public sealed record StartCombatantInput(
    string Id,
    string Name,
    bool IsPlayerCharacter = false,
    int CurrentHp = 0,
    int MaxHp = 0,
    int TemporaryHp = 0,
    int Initiative = 0);

/// <summary>Body for rolling initiative: combatant id → initiative value.</summary>
public sealed record RollInitiativeBody(Dictionary<string, int> Initiative);

/// <summary>Body for recording a death-saving throw.</summary>
public sealed record DeathSaveBody(string TargetId, bool Success, bool RecoversHp = false);

/// <summary>Body carrying just a target combatant id (e.g. elite/weak template application).</summary>
public sealed record TargetBody(string TargetId);

/// <summary>Body for damage/healing: a target combatant id and a non-negative amount.</summary>
public sealed record TargetAmountBody(string TargetId, int Amount);

/// <summary>Body for applying/removing a condition on a target combatant.</summary>
public sealed record ConditionBody(string TargetId, string ConditionName, int? RemainingRounds = null);
