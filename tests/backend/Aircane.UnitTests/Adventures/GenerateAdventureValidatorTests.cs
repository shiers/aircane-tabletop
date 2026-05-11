using Aircane.Application.DTOs.Adventures;
using Aircane.Application.Validation;
using FluentValidation.TestHelper;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class GenerateAdventureValidatorTests
{
    private readonly GenerateAdventureValidator _validator = new();

    private static GenerateAdventureRequest ValidRequest() => new()
    {
        Mode = "Solo",
        Ruleset = "D&D 5e 2014",
        GameSystem = "D&D 5e 2014",
        PartySize = 1,
        AverageLevel = 5,
        Tone = "epic",
        Length = "one-shot",
        Difficulty = "medium",
        CombatRatio = 34,
        ExplorationRatio = 33,
        RoleplayRatio = 33,
    };

    [Fact]
    public void Valid_solo_request_passes()
    {
        var result = _validator.TestValidate(ValidRequest());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Valid_group_request_passes()
    {
        var request = ValidRequest() with { Mode = "Group", PartySize = 4 };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Campaign")]
    [InlineData("invalid")]
    public void Invalid_mode_fails(string mode)
    {
        var request = ValidRequest() with { Mode = mode };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Mode);
    }

    [Fact]
    public void Empty_ruleset_fails()
    {
        var request = ValidRequest() with { Ruleset = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Ruleset);
    }

    [Fact]
    public void Empty_game_system_fails()
    {
        var request = ValidRequest() with { GameSystem = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GameSystem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Invalid_party_size_fails(int partySize)
    {
        var request = ValidRequest() with { PartySize = partySize };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PartySize);
    }

    [Fact]
    public void Group_mode_requires_party_size_at_least_2()
    {
        var request = ValidRequest() with { Mode = "Group", PartySize = 1 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PartySize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void Invalid_average_level_fails(int level)
    {
        var request = ValidRequest() with { AverageLevel = level };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AverageLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void Valid_average_level_passes(int level)
    {
        var request = ValidRequest() with { AverageLevel = level };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.AverageLevel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("silly")]
    [InlineData("INVALID")]
    public void Invalid_tone_fails(string tone)
    {
        var request = ValidRequest() with { Tone = tone };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Tone);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("lighthearted")]
    [InlineData("epic")]
    [InlineData("horror")]
    [InlineData("comedic")]
    [InlineData("mysterious")]
    [InlineData("heroic")]
    public void Valid_tone_passes(string tone)
    {
        var request = ValidRequest() with { Tone = tone };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Tone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("epic")]
    [InlineData("forever")]
    public void Invalid_length_fails(string length)
    {
        var request = ValidRequest() with { Length = length };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Length);
    }

    [Theory]
    [InlineData("one-shot")]
    [InlineData("short")]
    [InlineData("medium")]
    [InlineData("long")]
    public void Valid_length_passes(string length)
    {
        var request = ValidRequest() with { Length = length };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("impossible")]
    [InlineData("trivial")]
    public void Invalid_difficulty_fails(string difficulty)
    {
        var request = ValidRequest() with { Difficulty = difficulty };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Difficulty);
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("medium")]
    [InlineData("hard")]
    [InlineData("deadly")]
    public void Valid_difficulty_passes(string difficulty)
    {
        var request = ValidRequest() with { Difficulty = difficulty };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Difficulty);
    }

    [Fact]
    public void Ratios_summing_to_100_passes()
    {
        var request = ValidRequest() with
        {
            CombatRatio = 50,
            ExplorationRatio = 30,
            RoleplayRatio = 20,
        };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ratios_within_tolerance_of_5_passes()
    {
        // Sum = 97, within ±5 of 100
        var request = ValidRequest() with
        {
            CombatRatio = 30,
            ExplorationRatio = 34,
            RoleplayRatio = 33,
        };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ratios_summing_far_from_100_fails()
    {
        var request = ValidRequest() with
        {
            CombatRatio = 50,
            ExplorationRatio = 50,
            RoleplayRatio = 50,
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("Combat, exploration, and roleplay ratios must sum to approximately 100 (within ±5).");
    }

    [Fact]
    public void Ratios_summing_to_90_fails()
    {
        var request = ValidRequest() with
        {
            CombatRatio = 30,
            ExplorationRatio = 30,
            RoleplayRatio = 30,
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("Combat, exploration, and roleplay ratios must sum to approximately 100 (within ±5).");
    }

    [Fact]
    public void Combat_ratio_below_0_fails()
    {
        var request = ValidRequest() with { CombatRatio = -1 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CombatRatio);
    }

    [Fact]
    public void Combat_ratio_above_100_fails()
    {
        var request = ValidRequest() with { CombatRatio = 101 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CombatRatio);
    }

    [Fact]
    public void Setting_over_500_chars_fails()
    {
        var request = ValidRequest() with { Setting = new string('x', 501) };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Setting);
    }

    [Fact]
    public void Null_setting_passes()
    {
        var request = ValidRequest() with { Setting = null };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Setting);
    }

    [Fact]
    public void More_than_10_character_ids_fails()
    {
        var ids = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();
        var request = ValidRequest() with { CharacterIds = ids };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CharacterIds);
    }

    [Fact]
    public void Null_character_ids_passes()
    {
        var request = ValidRequest() with { CharacterIds = null };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.CharacterIds);
    }

    [Fact]
    public void Case_insensitive_mode_passes()
    {
        var request = ValidRequest() with { Mode = "solo" };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Mode);
    }

    [Fact]
    public void Case_insensitive_tone_passes()
    {
        var request = ValidRequest() with { Tone = "EPIC" };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Tone);
    }
}
