using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Dice;
using Aircane.Application.GameSystems;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Dice rolls for a session - regular expression rolls, manual physical dice entry, and roll log.
/// Supports system-aware resolution using the campaign's bound dice convention.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId:guid}")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class RollsController : ControllerBase
{
    private readonly IDiceService _dice;
    private readonly ISystemRegistry _registry;
    private readonly IMechanicResolver _mechanicResolver;
    private readonly ILogger<RollsController> _logger;

    public RollsController(
        IDiceService dice,
        ISystemRegistry registry,
        IMechanicResolver mechanicResolver,
        ILogger<RollsController> logger)
    {
        _dice = dice;
        _registry = registry;
        _mechanicResolver = mechanicResolver;
        _logger = logger;
    }

    // ── POST /api/sessions/{sessionId}/rolls ──────────────────────────────────

    /// <summary>
    /// Rolls a dice expression within a session and persists the result.
    /// When a campaign is bound to a Game System Definition, resolves using the system's dice convention
    /// and includes outcomeTier when the resolution rule defines degrees of success.
    /// </summary>
    [HttpPost("rolls")]
    [ProducesResponseType(typeof(SystemAwareRollDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RollDice(
        Guid sessionId,
        [FromBody] RollDiceBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Formula))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Formula is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (body.RollerParticipantId == Guid.Empty)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "RollerParticipantId is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        var parsed = _dice.ParseExpression(body.Formula);
        if (!parsed.IsValid)
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid dice expression",
                Detail = parsed.ParseError,
                Status = StatusCodes.Status400BadRequest,
            });

        var request = new RollRequest(
            SessionId: sessionId,
            RollerParticipantId: body.RollerParticipantId,
            Formula: body.Formula,
            Visibility: body.Visibility,
            CharacterId: body.CharacterId,
            Context: body.Context);

        var dto = await _dice.RollAsync(request, cancellationToken);

        // Attempt system-aware resolution if a campaign is bound
        string? outcomeTier = null;
        int? successCount = null;
        IReadOnlyList<int>? rawResults = null;

        if (body.CampaignId.HasValue)
        {
            try
            {
                var definition = await _registry.GetByCampaignAsync(body.CampaignId.Value, cancellationToken);
                var convention = definition.DiceConventions.FirstOrDefault(c =>
                    c.Name.Equals("primary", StringComparison.OrdinalIgnoreCase))
                    ?? definition.DiceConventions.FirstOrDefault();

                if (convention is not null)
                {
                    var parseResult = _mechanicResolver.ParseExpression(body.Formula, convention);
                    if (parseResult.IsSuccess)
                    {
                        var random = new DefaultRandomSource();
                        var rollResolution = _mechanicResolver.Roll(parseResult.Expression!, convention, random);
                        outcomeTier = rollResolution.OutcomeTier;
                        successCount = rollResolution.SuccessCount;
                        rawResults = rollResolution.RawResults;

                        // If a resolution rule is specified, resolve the outcome
                        if (!string.IsNullOrWhiteSpace(body.ResolutionRuleName))
                        {
                            var rule = definition.ResolutionRules.FirstOrDefault(r =>
                                r.Name.Equals(body.ResolutionRuleName, StringComparison.OrdinalIgnoreCase));

                            if (rule is not null)
                            {
                                var characterState = body.TargetNumber.HasValue
                                    ? new CharacterState { Attributes = new Dictionary<string, int> { [rule.TargetSource ?? "dc"] = body.TargetNumber.Value } }
                                    : null;

                                var outcome = _mechanicResolver.Resolve(rollResolution, rule, characterState);
                                if (outcome.Error is null)
                                {
                                    outcomeTier = outcome.Outcome;
                                }
                            }
                        }
                    }
                }
            }
            catch (KeyNotFoundException)
            {
                // No definition bound - fall through to standard result
                _logger.LogDebug("No game system definition bound for campaign {CampaignId}", body.CampaignId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "System-aware resolution failed for campaign {CampaignId}, falling back to standard",
                    body.CampaignId);
            }
        }

        var result = new SystemAwareRollDto(
            Id: dto.Id,
            SessionId: dto.SessionId,
            CharacterId: dto.CharacterId,
            RollerParticipantId: dto.RollerParticipantId,
            Formula: dto.Formula,
            DieResults: dto.DieResults,
            Modifier: dto.Modifier,
            Total: dto.Total,
            IsManual: dto.IsManual,
            Visibility: dto.Visibility,
            Context: dto.Context,
            CreatedAt: dto.CreatedAt,
            OutcomeTier: outcomeTier,
            SuccessCount: successCount,
            RawResults: rawResults);

        return CreatedAtAction(nameof(GetRollLog), new { sessionId }, result);
    }

    // ── POST /api/sessions/{sessionId}/manual-rolls ───────────────────────────

    /// <summary>
    /// Records a manually entered physical dice roll.
    /// The total is authoritative - no server-side re-roll is performed.
    /// </summary>
    [HttpPost("manual-rolls")]
    [ProducesResponseType(typeof(RollDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordManualRoll(
        Guid sessionId,
        [FromBody] ManualRollBody body,
        CancellationToken cancellationToken)
    {
        if (body.RollerParticipantId == Guid.Empty)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "RollerParticipantId is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        if (body.DieResults is null || body.DieResults.Length == 0)
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "DieResults must not be empty.",
                Status = StatusCodes.Status400BadRequest,
            });

        var request = new ManualRollRequest(
            SessionId: sessionId,
            RollerParticipantId: body.RollerParticipantId,
            DieResults: body.DieResults,
            Modifier: body.Modifier,
            Total: body.Total,
            Formula: body.Formula,
            Visibility: body.Visibility,
            CharacterId: body.CharacterId,
            Context: body.Context);

        var dto = await _dice.RecordManualRollAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetRollLog), new { sessionId }, dto);
    }

    // ── GET /api/sessions/{sessionId}/rolls ───────────────────────────────────

    /// <summary>
    /// Returns the roll log for a session, ordered by most recent first.
    /// </summary>
    [HttpGet("rolls")]
    [ProducesResponseType(typeof(IReadOnlyList<RollDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRollLog(
        Guid sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var rolls = await _dice.GetRollLogAsync(sessionId, page, pageSize, cancellationToken);
        return Ok(rolls);
    }
}

// ── Request body types ────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/sessions/{sessionId}/rolls.</summary>
public sealed record RollDiceBody(
    Guid RollerParticipantId,
    string Formula,
    RollVisibility Visibility = RollVisibility.Public,
    Guid? CharacterId = null,
    string? Context = null,
    Guid? CampaignId = null,
    string? ResolutionRuleName = null,
    int? TargetNumber = null);

/// <summary>Request body for POST /api/sessions/{sessionId}/manual-rolls.</summary>
public sealed record ManualRollBody(
    Guid RollerParticipantId,
    int[] DieResults,
    int Modifier,
    int Total,
    string? Formula = null,
    RollVisibility Visibility = RollVisibility.Public,
    Guid? CharacterId = null,
    string? Context = null);

/// <summary>
/// Extended roll DTO that includes system-aware resolution data.
/// </summary>
public sealed record SystemAwareRollDto(
    Guid Id,
    Guid SessionId,
    Guid? CharacterId,
    Guid RollerParticipantId,
    string? Formula,
    int[] DieResults,
    int Modifier,
    int Total,
    bool IsManual,
    RollVisibility Visibility,
    string? Context,
    DateTimeOffset CreatedAt,
    string? OutcomeTier = null,
    int? SuccessCount = null,
    IReadOnlyList<int>? RawResults = null);
