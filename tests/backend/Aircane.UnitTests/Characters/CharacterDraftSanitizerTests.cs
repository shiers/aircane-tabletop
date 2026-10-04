using Aircane.Application.Characters;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters;

/// <summary>
/// Unit tests for <see cref="CharacterDraftSanitizer"/>: every clamp brings a value into the strict
/// <see cref="CharacterSchemaValidator"/> range and records exactly one warning + one review path,
/// while an in-range character is left untouched.
/// </summary>
public sealed class CharacterDraftSanitizerTests
{
    private static CanonicalCharacter ValidCharacter()
    {
        var c = new CanonicalCharacter();
        c.Identity.Name = "In Range";
        c.Classes.Add(new CharacterClass { ClassName = "Fighter", Level = 5, HitDie = 10 });
        c.Abilities.Strength = 16;
        c.Abilities.Dexterity = 14;
        c.Abilities.Constitution = 15;
        c.Abilities.Intelligence = 10;
        c.Abilities.Wisdom = 12;
        c.Abilities.Charisma = 8;
        c.Combat.MaxHitPoints = 44;
        c.Combat.CurrentHitPoints = 30;
        c.Combat.TemporaryHitPoints = 0;
        return c;
    }

    [Fact]
    public void InRangeCharacter_IsUntouched_NoWarningsOrPaths()
    {
        var c = ValidCharacter();

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.True(result.IsClean);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.ReviewPaths);
        Assert.Equal(16, c.Abilities.Strength);
        Assert.Equal(44, c.Combat.MaxHitPoints);
        Assert.Equal(30, c.Combat.CurrentHitPoints);
        Assert.Equal(5, c.Classes[0].Level);
    }

    [Fact]
    public void NegativeCurrentHitPoints_FlooredToZero()
    {
        var c = ValidCharacter();
        c.Combat.CurrentHitPoints = -7;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(0, c.Combat.CurrentHitPoints);
        Assert.Single(result.Warnings);
        Assert.Equal(new[] { CanonicalCharacterPathForCurrentHp() }, result.ReviewPaths);
    }

    [Fact]
    public void CurrentHitPoints_CappedAtMax()
    {
        var c = ValidCharacter();
        c.Combat.MaxHitPoints = 20;
        c.Combat.CurrentHitPoints = 50;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(20, c.Combat.CurrentHitPoints);
        Assert.Single(result.Warnings);
        Assert.Single(result.ReviewPaths);
    }

    [Fact]
    public void NegativeMaxHitPoints_FlooredToZero_AndCurrentFollows()
    {
        var c = ValidCharacter();
        c.Combat.MaxHitPoints = -5;
        c.Combat.CurrentHitPoints = 3; // will be clamped to Min(current, max) = 0

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(0, c.Combat.MaxHitPoints);
        Assert.Equal(0, c.Combat.CurrentHitPoints);
        // Two distinct fields changed → two warnings and two paths.
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal(2, result.ReviewPaths.Count);
    }

    [Fact]
    public void NegativeTemporaryHitPoints_FlooredToZero()
    {
        var c = ValidCharacter();
        c.Combat.TemporaryHitPoints = -3;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(0, c.Combat.TemporaryHitPoints);
        Assert.Single(result.Warnings);
        Assert.Equal(new[] { "combat.temporaryHitPoints" }, result.ReviewPaths);
    }

    [Fact]
    public void AbilityScore_ZeroRaisedToOne()
    {
        var c = ValidCharacter();
        c.Abilities.Strength = 0;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(1, c.Abilities.Strength);
        Assert.Single(result.Warnings);
        Assert.Equal(new[] { CanonicalCharacterPaths.AbilityStrength }, result.ReviewPaths);
    }

    [Fact]
    public void AbilityScore_FortyClampedToThirty()
    {
        var c = ValidCharacter();
        c.Abilities.Charisma = 40;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(30, c.Abilities.Charisma);
        Assert.Single(result.Warnings);
        Assert.Equal(new[] { CanonicalCharacterPaths.AbilityCharisma }, result.ReviewPaths);
    }

    [Fact]
    public void ClassLevel_ZeroRaisedToOne()
    {
        var c = ValidCharacter();
        c.Classes[0].Level = 0;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(1, c.Classes[0].Level);
        Assert.Single(result.Warnings);
        Assert.Single(result.ReviewPaths);
    }

    [Fact]
    public void ClassLevel_TwentyFiveClampedToTwenty()
    {
        var c = ValidCharacter();
        c.Classes[0].Level = 25;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(20, c.Classes[0].Level);
        Assert.Single(result.Warnings);
        Assert.Single(result.ReviewPaths);
    }

    [Fact]
    public void MulticlassSumAboveTwenty_ClampedToAtMostTwenty()
    {
        var c = ValidCharacter();
        c.Classes.Clear();
        c.Classes.Add(new CharacterClass { ClassName = "Fighter", Level = 15, HitDie = 10 });
        c.Classes.Add(new CharacterClass { ClassName = "Wizard", Level = 12, HitDie = 6 });

        var result = CharacterDraftSanitizer.Sanitize(c);

        var total = c.Classes.Sum(x => x.Level);
        Assert.True(total <= 20, $"total was {total}");
        Assert.All(c.Classes, cls => Assert.True(cls.Level >= 1));
        // Primary class keeps its level; trailing class reduced; plus the total-level warning.
        Assert.Equal(15, c.Classes[0].Level);
        Assert.Contains(result.ReviewPaths, p => p == CanonicalCharacterPaths.Level);
    }

    [Fact]
    public void NonPositiveHitDie_SetToClassDefault()
    {
        var c = ValidCharacter();
        c.Classes[0].ClassName = "Barbarian";
        c.Classes[0].HitDie = 0;

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(12, c.Classes[0].HitDie); // Barbarian → d12
        Assert.Single(result.Warnings);
        Assert.Equal(new[] { CanonicalCharacterPaths.Class }, result.ReviewPaths);
    }

    [Fact]
    public void UnknownClassHitDie_DefaultsToEight()
    {
        var c = ValidCharacter();
        c.Classes[0].ClassName = "Mystic";
        c.Classes[0].HitDie = -2;

        CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(8, c.Classes[0].HitDie);
    }

    [Fact]
    public void EmptyClasses_LeftForValidatorToReject()
    {
        var c = ValidCharacter();
        c.Classes.Clear();

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Empty(c.Classes);
        // No class-related warnings when there are no classes to clamp.
        Assert.DoesNotContain(result.Warnings, w => w.Contains("level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EachClamp_YieldsExactlyOneWarningAndOneReviewPath()
    {
        var c = ValidCharacter();
        c.Abilities.Strength = 0;        // 1 change
        c.Abilities.Charisma = 99;       // 1 change
        c.Combat.CurrentHitPoints = -1;  // 1 change

        var result = CharacterDraftSanitizer.Sanitize(c);

        Assert.Equal(3, result.Warnings.Count);
        Assert.Equal(3, result.ReviewPaths.Count);
    }

    private static string CanonicalCharacterPathForCurrentHp() => CanonicalCharacterPaths.CombatHitPoints;
}
