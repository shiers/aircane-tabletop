using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.Dice;
using Aircane.Application.DTOs.Dice;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Dice;

/// <summary>
/// EF Core-backed implementation of <see cref="IDiceService"/>.
/// Parses dice expressions, generates cryptographically random results,
/// persists rolls, and returns the full roll log.
/// </summary>
public sealed class DiceService : IDiceService
{
    private readonly AircaneDbContext _db;
    private readonly IRollRandomizer _randomizer;
    private readonly ILogger<DiceService> _logger;

    public DiceService(
        AircaneDbContext db,
        IRollRandomizer randomizer,
        ILogger<DiceService> logger)
    {
        _db = db;
        _randomizer = randomizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public ParsedDiceExpression ParseExpression(string formula)
    {
        if (DiceParser.TryParse(formula, out var expr))
        {
            return new ParsedDiceExpression(
                OriginalFormula: formula,
                DiceCount: expr!.DiceCount,
                DiceSides: expr.DieSides,
                Modifier: expr.Modifier,
                IsValid: true);
        }

        return new ParsedDiceExpression(
            OriginalFormula: formula,
            DiceCount: 0,
            DiceSides: 0,
            Modifier: 0,
            IsValid: false,
            ParseError: $"'{formula}' is not a valid dice expression.");
    }

    /// <inheritdoc />
    public async Task<RollDto> RollAsync(
        RollRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = ExecuteRoll(request);

        var roll = new Roll(
            sessionId: request.SessionId,
            rollerParticipantId: request.RollerParticipantId,
            formula: request.Formula,
            dieResultsJson: JsonSerializer.Serialize(result.AllDieResults),
            modifier: result.Roll.Modifier,
            total: result.Total,
            visibility: request.Visibility,
            isManual: false,
            characterId: request.CharacterId,
            context: request.Context);

        _db.Rolls.Add(roll);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Roll persisted: {RollId} formula={Formula} total={Total} session={SessionId}",
            roll.Id, request.Formula, result.Total, request.SessionId);

        return ToDto(roll, result.AllDieResults);
    }

    /// <inheritdoc />
    public async Task<RollDto> RecordManualRollAsync(
        ManualRollRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var roll = new Roll(
            sessionId: request.SessionId,
            rollerParticipantId: request.RollerParticipantId,
            formula: request.Formula,
            dieResultsJson: JsonSerializer.Serialize(request.DieResults),
            modifier: request.Modifier,
            total: request.Total,
            visibility: request.Visibility,
            isManual: true,
            characterId: request.CharacterId,
            context: request.Context);

        _db.Rolls.Add(roll);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Manual roll persisted: {RollId} formula={Formula} total={Total} session={SessionId}",
            roll.Id, request.Formula, request.Total, request.SessionId);

        return ToDto(roll, request.DieResults);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RollDto>> GetRollLogAsync(
        Guid sessionId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var rolls = await _db.Rolls
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return rolls
            .Select(r => ToDto(r, DeserializeDieResults(r.DieResultsJson)))
            .ToList();
    }

    // ── Internal roll execution ───────────────────────────────────────────────

    private RollResult ExecuteRoll(RollRequest request)
    {
        var expr = DiceParser.Parse(request.Formula);

        // Roll all dice
        var allResults = new int[expr.DiceCount];
        for (var i = 0; i < expr.DiceCount; i++)
            allResults[i] = _randomizer.RollDie(expr.DieSides);

        // Apply keep-highest / keep-lowest logic
        int[] keptResults;
        int[] droppedResults;

        if (expr.KeepHighest.HasValue)
        {
            var sorted = allResults.OrderByDescending(x => x).ToArray();
            keptResults = sorted.Take(expr.KeepHighest.Value).ToArray();
            droppedResults = sorted.Skip(expr.KeepHighest.Value).ToArray();
        }
        else if (expr.KeepLowest.HasValue)
        {
            var sorted = allResults.OrderBy(x => x).ToArray();
            keptResults = sorted.Take(expr.KeepLowest.Value).ToArray();
            droppedResults = sorted.Skip(expr.KeepLowest.Value).ToArray();
        }
        else
        {
            keptResults = allResults;
            droppedResults = [];
        }

        var total = keptResults.Sum() + expr.Modifier;

        // Build a placeholder RollDto (Id/CreatedAt will be set after persistence)
        var rollDto = new RollDto(
            Id: Guid.Empty,
            SessionId: request.SessionId,
            CharacterId: request.CharacterId,
            RollerParticipantId: request.RollerParticipantId,
            Formula: request.Formula,
            DieResults: allResults,
            Modifier: expr.Modifier,
            Total: total,
            IsManual: false,
            Visibility: request.Visibility,
            Context: request.Context,
            CreatedAt: DateTimeOffset.UtcNow);

        return new RollResult(rollDto, allResults, keptResults, droppedResults, total);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static RollDto ToDto(Roll roll, int[] dieResults) => new(
        Id: roll.Id,
        SessionId: roll.SessionId,
        CharacterId: roll.CharacterId,
        RollerParticipantId: roll.RollerParticipantId,
        Formula: roll.Formula,
        DieResults: dieResults,
        Modifier: roll.Modifier,
        Total: roll.Total,
        IsManual: roll.IsManual,
        Visibility: roll.Visibility,
        Context: roll.Context,
        CreatedAt: roll.CreatedAt);

    private static int[] DeserializeDieResults(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
