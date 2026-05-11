using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Adventures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class Dnd5eEncounterValidatorTests
{
    private readonly Dnd5eEncounterValidator _validator = new(NullLogger<Dnd5eEncounterValidator>.Instance);

    // ── CR Parsing ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0", 10)]
    [InlineData("1/8", 25)]
    [InlineData("1/4", 50)]
    [InlineData("1/2", 100)]
    [InlineData("1", 200)]
    [InlineData("5", 1800)]
    [InlineData("10", 5900)]
    [InlineData("20", 25000)]
    [InlineData("30", 155000)]
    public void TryParseCrToXp_parses_standard_cr_values(string cr, int expectedXp)
    {
        Assert.True(Dnd5eEncounterValidator.TryParseCrToXp(cr, out var xp));
        Assert.Equal(expectedXp, xp);
    }

    [Theory]
    [InlineData("CR 5", 1800)]
    [InlineData("cr 1/2", 100)]
    [InlineData("CR 0", 10)]
    [InlineData("CR 20", 25000)]
    public void TryParseCrToXp_handles_cr_prefix(string cr, int expectedXp)
    {
        Assert.True(Dnd5eEncounterValidator.TryParseCrToXp(cr, out var xp));
        Assert.Equal(expectedXp, xp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("N/A")]
    [InlineData("varies")]
    public void TryParseCrToXp_returns_false_for_invalid_values(string? cr)
    {
        Assert.False(Dnd5eEncounterValidator.TryParseCrToXp(cr, out _));
    }

    // ── Encounter Multiplier ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 4, 1.0)]
    [InlineData(2, 4, 1.5)]
    [InlineData(3, 4, 2.0)]
    [InlineData(6, 4, 2.0)]
    [InlineData(7, 4, 2.5)]
    [InlineData(10, 4, 2.5)]
    [InlineData(11, 4, 3.0)]
    [InlineData(15, 4, 4.0)]
    public void GetEncounterMultiplier_standard_party_returns_correct_multiplier(int monsterCount, int partySize, double expected)
    {
        Assert.Equal(expected, Dnd5eEncounterValidator.GetEncounterMultiplier(monsterCount, partySize));
    }

    [Fact]
    public void GetEncounterMultiplier_small_party_shifts_up()
    {
        // Party of 2, 1 monster: should shift up one bracket (1.0 -> 1.5)
        Assert.Equal(1.5, Dnd5eEncounterValidator.GetEncounterMultiplier(1, 2));
    }

    [Fact]
    public void GetEncounterMultiplier_large_party_shifts_down()
    {
        // Party of 6, 2 monsters: should shift down one bracket (1.5 -> 1.0)
        Assert.Equal(1.0, Dnd5eEncounterValidator.GetEncounterMultiplier(2, 6));
    }

    // ── Party Thresholds ──────────────────────────────────────────────────────

    [Fact]
    public void GetPartyThresholds_level_1_party_of_4()
    {
        var (easy, medium, hard, deadly) = Dnd5eEncounterValidator.GetPartyThresholds(4, 1);

        Assert.Equal(100, easy);    // 25 * 4
        Assert.Equal(200, medium);  // 50 * 4
        Assert.Equal(300, hard);    // 75 * 4
        Assert.Equal(400, deadly);  // 100 * 4
    }

    [Fact]
    public void GetPartyThresholds_level_5_party_of_4()
    {
        var (easy, medium, hard, deadly) = Dnd5eEncounterValidator.GetPartyThresholds(4, 5);

        Assert.Equal(1000, easy);   // 250 * 4
        Assert.Equal(2000, medium); // 500 * 4
        Assert.Equal(3000, hard);   // 750 * 4
        Assert.Equal(4400, deadly); // 1100 * 4
    }

    // ── Full Encounter Validation ─────────────────────────────────────────────

    [Fact]
    public void ValidateEncounter_medium_encounter_for_level_5_party_of_4()
    {
        // 4 goblins (CR 1/4 = 50 XP each) = 200 base XP
        // 4 monsters -> x2.0 multiplier = 400 adjusted XP
        // Party of 4 at level 5: medium threshold = 2000
        // 400 < 1000 (easy) -> trivial
        var encounter = CreateEncounter("easy", ("Goblin", 4, "1/4"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 5);

        Assert.Equal("trivial", result.EstimatedDifficulty);
        Assert.Equal(200, result.TotalXP);
    }

    [Fact]
    public void ValidateEncounter_hard_encounter_for_level_3_party_of_4()
    {
        // 1 Owlbear (CR 3 = 700 XP)
        // 1 monster -> x1.0 multiplier = 700 adjusted XP
        // Party of 4 at level 3: hard threshold = 900, medium = 600
        // 700 >= 600 (medium) -> medium
        var encounter = CreateEncounter("hard", ("Owlbear", 1, "3"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 3);

        Assert.Equal("medium", result.EstimatedDifficulty);
        Assert.Equal(700, result.TotalXP);
        Assert.Equal(700, result.AdjustedXP);
    }

    [Fact]
    public void ValidateEncounter_deadly_encounter_flags_warning()
    {
        // 2 Young Dragons (CR 10 = 5900 XP each) = 11800 base XP
        // 2 monsters -> x1.5 multiplier = 17700 adjusted XP
        // Party of 4 at level 5: deadly threshold = 4400
        // Way above deadly
        var encounter = CreateEncounter("medium", ("Young Red Dragon", 2, "10"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 5);

        Assert.Equal("deadly", result.EstimatedDifficulty);
        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("significantly harder"));
    }

    [Fact]
    public void ValidateEncounter_trivial_encounter_flags_too_easy()
    {
        // 1 Rat (CR 0 = 10 XP)
        // Party of 4 at level 10: easy threshold = 2400
        // Way below easy
        var encounter = CreateEncounter("hard", ("Giant Rat", 1, "0"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 10);

        Assert.Equal("trivial", result.EstimatedDifficulty);
        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("significantly easier"));
    }

    [Fact]
    public void ValidateEncounter_matching_difficulty_is_valid()
    {
        // 3 Bugbears (CR 1 = 200 XP each) = 600 base XP
        // 3 monsters -> x2.0 multiplier = 1200 adjusted XP
        // Party of 4 at level 3: medium = 600, hard = 900, deadly = 1600
        // 1200 >= 900 (hard) -> hard
        var encounter = CreateEncounter("hard", ("Bugbear", 3, "1"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 3);

        Assert.Equal("hard", result.EstimatedDifficulty);
        Assert.True(result.IsValid);
        Assert.Equal(600, result.TotalXP);
        Assert.Equal(1200, result.AdjustedXP);
    }

    [Fact]
    public void ValidateEncounter_unparsable_cr_adds_warning()
    {
        // Mix of parsable and unparsable CRs — the unparsable one should produce a warning
        var encounter = new GeneratedEncounter
        {
            Title = "Mixed Encounter",
            SceneId = "scene-1",
            Enemies =
            [
                new EncounterCreature { Name = "Goblin", Count = 2, ChallengeRating = "1/4" },
                new EncounterCreature { Name = "Mysterious Entity", Count = 1, ChallengeRating = "varies" },
            ],
            Difficulty = "medium",
            Tactics = "Standard tactics.",
        };

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 5);

        Assert.Contains(result.Warnings, w => w.Contains("Could not parse CR"));
    }

    [Fact]
    public void ValidateEncounter_all_unparsable_returns_unknown_difficulty()
    {
        var encounter = CreateEncounter("medium", ("Eldritch Horror", 2, "N/A"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 5);

        Assert.Equal("unknown", result.EstimatedDifficulty);
        Assert.True(result.IsValid);
        Assert.Equal(0, result.TotalXP);
    }

    [Fact]
    public void ValidateEncounter_deadly_for_small_party_adds_warning()
    {
        // 1 Adult Dragon (CR 17 = 18000 XP)
        // Party of 1 at level 5: deadly threshold = 1100
        // Way above deadly for a solo player
        var encounter = CreateEncounter("deadly", ("Adult Red Dragon", 1, "17"));

        var result = _validator.ValidateEncounter(encounter, partySize: 1, averageLevel: 5);

        Assert.Equal("deadly", result.EstimatedDifficulty);
        Assert.Contains(result.Warnings, w => w.Contains("small party"));
    }

    [Fact]
    public void ValidateEncounter_no_enemies_is_invalid()
    {
        var encounter = new GeneratedEncounter
        {
            Title = "Empty Room",
            SceneId = "scene-1",
            Enemies = [],
            Difficulty = "medium",
            Tactics = "None",
        };

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 5);

        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("no enemies"));
    }

    [Fact]
    public void ValidateEncounter_mixed_cr_creatures()
    {
        // 1 Ogre (CR 2 = 450 XP) + 4 Goblins (CR 1/4 = 50 XP each) = 650 base XP
        // 5 monsters -> x2.0 multiplier = 1300 adjusted XP
        // Party of 4 at level 3: hard = 900, deadly = 1600
        // 1300 >= 900 -> hard
        var encounter = new GeneratedEncounter
        {
            Title = "Ogre and Goblin Ambush",
            SceneId = "scene-2",
            Enemies =
            [
                new EncounterCreature { Name = "Ogre", Count = 1, ChallengeRating = "2" },
                new EncounterCreature { Name = "Goblin", Count = 4, ChallengeRating = "1/4" },
            ],
            Difficulty = "hard",
            Tactics = "Goblins harass from range while ogre charges.",
        };

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 3);

        Assert.Equal("hard", result.EstimatedDifficulty);
        Assert.True(result.IsValid);
        Assert.Equal(650, result.TotalXP);
        Assert.Equal(1300, result.AdjustedXP);
    }

    [Fact]
    public void ValidateEncounter_level_1_party_deadly_encounter()
    {
        // 1 Bugbear (CR 1 = 200 XP)
        // 1 monster, party of 4 at level 1: deadly = 400
        // 200 >= 200 (medium) -> medium
        var encounter = CreateEncounter("medium", ("Bugbear", 1, "1"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 1);

        Assert.Equal("medium", result.EstimatedDifficulty);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateEncounter_clamps_level_to_valid_range()
    {
        var encounter = CreateEncounter("medium", ("Goblin", 2, "1/4"));

        // Level 0 should be clamped to 1
        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 0);

        Assert.NotNull(result);
        Assert.NotEqual("unknown", result.EstimatedDifficulty);
    }

    [Fact]
    public void ValidateEncounter_slightly_harder_adds_informational_warning()
    {
        // 2 Ogres (CR 2 = 450 XP each) = 900 base XP
        // 2 monsters -> x1.5 multiplier = 1350 adjusted XP
        // Party of 4 at level 3: medium = 600, hard = 900, deadly = 1600
        // 1350 >= 900 -> hard (one step above medium)
        var encounter = CreateEncounter("medium", ("Ogre", 2, "2"));

        var result = _validator.ValidateEncounter(encounter, partySize: 4, averageLevel: 3);

        Assert.Equal("hard", result.EstimatedDifficulty);
        Assert.True(result.IsValid); // Only 1 step off, still valid
        Assert.Contains(result.Warnings, w => w.Contains("slightly harder"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeneratedEncounter CreateEncounter(string difficulty, params (string Name, int Count, string Cr)[] creatures)
    {
        return new GeneratedEncounter
        {
            Title = "Test Encounter",
            SceneId = "scene-1",
            Enemies = creatures.Select(c => new EncounterCreature
            {
                Name = c.Name,
                Count = c.Count,
                ChallengeRating = c.Cr,
            }).ToList(),
            Difficulty = difficulty,
            Tactics = "Standard tactics.",
        };
    }
}
