using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Adventures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>
/// Unit tests for <see cref="Pf2eEncounterValidator"/>: the PF2e creature-level XP budget and
/// Trivial/Low/Moderate/Severe/Extreme tier classification.
/// </summary>
public class Pf2eEncounterValidatorTests
{
    private readonly Pf2eEncounterValidator _sut = new(NullLogger<Pf2eEncounterValidator>.Instance);

    private static GeneratedEncounter Encounter(string difficulty, params (string Level, int Count)[] enemies) => new()
    {
        Title = "Test",
        SceneId = "scene-1",
        Difficulty = difficulty,
        Tactics = "n/a",
        Enemies = enemies.Select(e => new EncounterCreature
        {
            Name = "Foe",
            Count = e.Count,
            ChallengeRating = e.Level,
        }).ToList(),
    };

    // ── XP-by-level-difference table ────────────────────────────────────────────

    [Theory]
    [InlineData(1, 5, 10)]   // -4 -> 10
    [InlineData(0, 5, 0)]    // -5 diff -> below range -> 0
    [InlineData(4, 5, 30)]   // -1 -> 30
    [InlineData(5, 5, 40)]   // +0 -> 40
    [InlineData(6, 5, 60)]   // +1 -> 60
    [InlineData(9, 5, 160)]  // +4 -> 160
    [InlineData(12, 5, 160)] // > +4 clamps to 160
    public void XpForCreatureLevel_MatchesTable(int creatureLevel, int partyLevel, int expected)
    {
        Assert.Equal(expected, Pf2eEncounterValidator.XpForCreatureLevel(creatureLevel, partyLevel));
    }

    [Theory]
    [InlineData("3", true, 3)]
    [InlineData("Level 5", true, 5)]
    [InlineData("Lvl -1", true, -1)]
    [InlineData("CL 7", true, 7)]
    [InlineData("boss", false, 0)]
    public void TryParseCreatureLevel_ParsesCommonForms(string input, bool ok, int expected)
    {
        var parsed = Pf2eEncounterValidator.TryParseCreatureLevel(input, out var level);
        Assert.Equal(ok, parsed);
        if (ok) Assert.Equal(expected, level);
    }

    // ── Party thresholds scale with party size ──────────────────────────────────

    [Fact]
    public void GetPartyThresholds_PartyOfFour_MatchesBaseline()
    {
        var (trivial, low, moderate, severe, extreme) = Pf2eEncounterValidator.GetPartyThresholds(4);
        Assert.Equal((40, 60, 80, 120, 160), (trivial, low, moderate, severe, extreme));
    }

    [Fact]
    public void GetPartyThresholds_LargerParty_IncreasesBudget()
    {
        var four = Pf2eEncounterValidator.GetPartyThresholds(4);
        var five = Pf2eEncounterValidator.GetPartyThresholds(5);
        Assert.True(five.Moderate > four.Moderate);
        Assert.True(five.Severe > four.Severe);
    }

    // ── Tier classification ──────────────────────────────────────────────────────

    [Fact]
    public void ValidateEncounter_ModerateBudget_ClassifiesModerate()
    {
        // Party of 4, level 5. Two level-5 foes = 40 + 40 = 80 XP == Moderate threshold.
        var result = _sut.ValidateEncounter(Encounter("moderate", ("5", 2)), partySize: 4, averageLevel: 5);
        Assert.Equal("moderate", result.EstimatedDifficulty);
        Assert.Equal(80, result.TotalXP);
    }

    [Fact]
    public void ValidateEncounter_SingleWeakFoe_ClassifiesTrivial()
    {
        // One level-1 foe vs level-5 party = -4 diff = 0 XP -> below Low -> trivial.
        var result = _sut.ValidateEncounter(Encounter("trivial", ("1", 1)), partySize: 4, averageLevel: 5);
        Assert.Equal("trivial", result.EstimatedDifficulty);
    }

    [Fact]
    public void ValidateEncounter_ExtremeBudget_ClassifiesExtreme()
    {
        // Party of 4, level 5. Enough high-level foes to exceed 160 XP.
        var result = _sut.ValidateEncounter(
            Encounter("extreme", ("9", 1), ("9", 1)), partySize: 4, averageLevel: 5); // 160 + 160
        Assert.Equal("extreme", result.EstimatedDifficulty);
    }

    [Fact]
    public void ValidateEncounter_MismatchTooHard_FlaggedInvalid()
    {
        // Requested trivial but built extreme.
        var result = _sut.ValidateEncounter(
            Encounter("trivial", ("9", 2)), partySize: 4, averageLevel: 5);
        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("harder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateEncounter_NoEnemies_Invalid()
    {
        var result = _sut.ValidateEncounter(Encounter("low"), partySize: 4, averageLevel: 5);
        Assert.False(result.IsValid);
    }
}
