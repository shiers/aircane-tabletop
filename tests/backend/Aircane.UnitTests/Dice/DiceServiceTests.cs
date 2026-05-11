using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Dice;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Dice;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Dice;

/// <summary>
/// Unit tests for <see cref="DiceService"/>.
/// Uses an in-memory EF Core database and a deterministic <see cref="IRollRandomizer"/>
/// so results are fully predictable.
/// </summary>
public class DiceServiceTests : IDisposable
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a fixed sequence of values, cycling when exhausted.
    /// </summary>
    private sealed class SequenceRandomizer : IRollRandomizer
    {
        private readonly int[] _values;
        private int _index;

        public SequenceRandomizer(params int[] values)
        {
            if (values.Length == 0)
                throw new ArgumentException("At least one value is required.", nameof(values));
            _values = values;
        }

        public int RollDie(int sides) => _values[_index++ % _values.Length];
    }

    private readonly AircaneDbContext _db;

    public DiceServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private DiceService CreateService(IRollRandomizer randomizer)
        => new(_db, randomizer, NullLogger<DiceService>.Instance);

    private static RollRequest SimpleRequest(
        string formula,
        RollVisibility visibility = RollVisibility.Public)
        => new(
            SessionId: Guid.NewGuid(),
            RollerParticipantId: Guid.NewGuid(),
            Formula: formula,
            Visibility: visibility);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollAsync_SimpleExpression_ProducesCorrectTotal()
    {
        // 1d6+2 where the die lands on 4 → total = 4 + 2 = 6
        var svc = CreateService(new SequenceRandomizer(4));
        var request = SimpleRequest("1d6+2");

        var result = await svc.RollAsync(request);

        Assert.Equal(6, result.Total);
        Assert.Equal(4, result.DieResults.Single());
        Assert.Equal(2, result.Modifier);
        Assert.Equal("1d6+2", result.Formula);
        Assert.False(result.IsManual);
    }

    [Fact]
    public async Task RollAsync_KeepHighest_DropsLowestDice()
    {
        // 4d6kh3 — rolls: 1, 5, 3, 6 → keep highest 3: 5, 3, 6 → total = 14
        var svc = CreateService(new SequenceRandomizer(1, 5, 3, 6));
        var request = SimpleRequest("4d6kh3");

        var result = await svc.RollAsync(request);

        // All four dice were rolled
        Assert.Equal(4, result.DieResults.Length);
        // Total is sum of the three highest (5+3+6 = 14)
        Assert.Equal(14, result.Total);
    }

    [Fact]
    public async Task RollAsync_Advantage_KeepsHigherOfTwoD20s()
    {
        // advantage = 2d20kh1 — rolls: 8, 17 → keep highest: 17
        var svc = CreateService(new SequenceRandomizer(8, 17));
        var request = SimpleRequest("advantage");

        var result = await svc.RollAsync(request);

        Assert.Equal(17, result.Total);
        Assert.Equal(2, result.DieResults.Length);
    }

    [Fact]
    public async Task RollAsync_Disadvantage_KeepsLowerOfTwoD20s()
    {
        // disadvantage = 2d20kl1 — rolls: 8, 17 → keep lowest: 8
        var svc = CreateService(new SequenceRandomizer(8, 17));
        var request = SimpleRequest("disadvantage");

        var result = await svc.RollAsync(request);

        Assert.Equal(8, result.Total);
        Assert.Equal(2, result.DieResults.Length);
    }

    [Fact]
    public async Task RollAsync_PersistsRollToDatabase()
    {
        var svc = CreateService(new SequenceRandomizer(10));
        var request = SimpleRequest("1d20+5");

        var result = await svc.RollAsync(request);

        var stored = await _db.Rolls.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.Equal(request.SessionId, stored!.SessionId);
        Assert.Equal("1d20+5", stored.Formula);
        Assert.Equal(15, stored.Total);
        Assert.False(stored.IsManual);
    }

    [Fact]
    public async Task RollAsync_InvalidFormula_ThrowsArgumentException()
    {
        var svc = CreateService(new SequenceRandomizer(1));
        var request = SimpleRequest("not-a-formula");

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RollAsync(request));
    }

    [Fact]
    public async Task RollAsync_MultipleRolls_AllPersistedAndReturnedInLog()
    {
        var sessionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var svc = CreateService(new SequenceRandomizer(3, 7, 12));

        await svc.RollAsync(new RollRequest(sessionId, participantId, "1d6"));
        await svc.RollAsync(new RollRequest(sessionId, participantId, "1d8"));
        await svc.RollAsync(new RollRequest(sessionId, participantId, "1d20"));

        var log = await svc.GetRollLogAsync(sessionId);

        Assert.Equal(3, log.Count);
        // Ordered most-recent first
        Assert.Equal("1d20", log[0].Formula);
        Assert.Equal("1d8", log[1].Formula);
        Assert.Equal("1d6", log[2].Formula);
    }

    [Fact]
    public async Task RollAsync_PrivateVisibility_StoredCorrectly()
    {
        var svc = CreateService(new SequenceRandomizer(15));
        var request = SimpleRequest("1d20", RollVisibility.Private);

        var result = await svc.RollAsync(request);

        Assert.Equal(RollVisibility.Private, result.Visibility);

        var stored = await _db.Rolls.FindAsync(result.Id);
        Assert.Equal(RollVisibility.Private, stored!.Visibility);
    }

    [Fact]
    public async Task RecordManualRollAsync_PersistsWithIsManualTrue()
    {
        var svc = CreateService(new SequenceRandomizer(1));
        var request = new ManualRollRequest(
            SessionId: Guid.NewGuid(),
            RollerParticipantId: Guid.NewGuid(),
            Formula: "1d20+3",
            DieResults: [18],
            Modifier: 3,
            Total: 21);

        var result = await svc.RecordManualRollAsync(request);

        Assert.True(result.IsManual);
        Assert.Equal(21, result.Total);
        Assert.Equal(18, result.DieResults.Single());

        var stored = await _db.Rolls.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsManual);
    }

    [Fact]
    public void ParseExpression_ValidFormula_ReturnsValidResult()
    {
        var svc = CreateService(new SequenceRandomizer(1));

        var parsed = svc.ParseExpression("2d6+3");

        Assert.True(parsed.IsValid);
        Assert.Equal(2, parsed.DiceCount);
        Assert.Equal(6, parsed.DiceSides);
        Assert.Equal(3, parsed.Modifier);
        Assert.Null(parsed.ParseError);
    }

    [Fact]
    public void ParseExpression_InvalidFormula_ReturnsInvalidResult()
    {
        var svc = CreateService(new SequenceRandomizer(1));

        var parsed = svc.ParseExpression("garbage");

        Assert.False(parsed.IsValid);
        Assert.NotNull(parsed.ParseError);
    }

    [Fact]
    public async Task GetRollLogAsync_DifferentSession_ReturnsOnlyMatchingRolls()
    {
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var svc = CreateService(new SequenceRandomizer(5));

        await svc.RollAsync(new RollRequest(sessionA, participantId, "1d6"));
        await svc.RollAsync(new RollRequest(sessionB, participantId, "1d6"));

        var logA = await svc.GetRollLogAsync(sessionA);
        var logB = await svc.GetRollLogAsync(sessionB);

        Assert.Single(logA);
        Assert.Single(logB);
        Assert.Equal(sessionA, logA[0].SessionId);
        Assert.Equal(sessionB, logB[0].SessionId);
    }
}
