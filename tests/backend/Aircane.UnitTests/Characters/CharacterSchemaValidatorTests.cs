using Aircane.Application.Characters;
using FluentValidation;
using Xunit;

namespace Aircane.UnitTests.Characters;

/// <summary>
/// Tests for <see cref="CharacterSchemaValidator"/>.
/// </summary>
public class CharacterSchemaValidatorTests
{
    private readonly CharacterSchemaValidator _validator = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CanonicalCharacter BuildValidCharacter() => new()
    {
        Identity = new CharacterIdentity { Name = "Aldric Stonehammer" },
        Classes = [new CharacterClass { ClassName = "Fighter", Level = 3, HitDie = 10 }],
        Abilities = new AbilityScores
        {
            Strength = 16, Dexterity = 12, Constitution = 14,
            Intelligence = 10, Wisdom = 11, Charisma = 9
        },
        Combat = new CombatStats
        {
            ArmorClass = 16,
            MaxHitPoints = 28,
            CurrentHitPoints = 28
        }
    };

    private bool IsValid(CanonicalCharacter character)
        => _validator.Validate(character).IsValid;

    private bool HasErrorFor(CanonicalCharacter character, string propertyNameFragment)
        => _validator.Validate(character).Errors
            .Any(e => e.PropertyName.Contains(propertyNameFragment, StringComparison.OrdinalIgnoreCase));

    // ── Valid character ───────────────────────────────────────────────────────

    [Fact]
    public void ValidCharacter_PassesValidation()
    {
        Assert.True(IsValid(BuildValidCharacter()));
    }

    // ── Name validation ───────────────────────────────────────────────────────

    [Fact]
    public void MissingName_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Identity.Name = string.Empty;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Name"));
    }

    [Fact]
    public void WhitespaceName_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Identity.Name = "   ";

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Name"));
    }

    [Fact]
    public void NameExceeding200Chars_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Identity.Name = new string('A', 201);

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Name"));
    }

    // ── Level / class validation ──────────────────────────────────────────────

    [Fact]
    public void NoClasses_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Classes.Clear();

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Classes"));
    }

    [Fact]
    public void ClassLevelZero_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Classes[0].Level = 0;

        Assert.False(IsValid(character));
    }

    [Fact]
    public void ClassLevelTwentyOne_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Classes[0].Level = 21;

        Assert.False(IsValid(character));
    }

    [Fact]
    public void MulticlassTotalExceeding20_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Classes =
        [
            new CharacterClass { ClassName = "Fighter", Level = 15, HitDie = 10 },
            new CharacterClass { ClassName = "Wizard", Level = 10, HitDie = 6 }
        ];

        Assert.False(IsValid(character));
    }

    [Fact]
    public void MulticlassTotalExactly20_PassesValidation()
    {
        var character = BuildValidCharacter();
        character.Classes =
        [
            new CharacterClass { ClassName = "Fighter", Level = 10, HitDie = 10 },
            new CharacterClass { ClassName = "Wizard", Level = 10, HitDie = 6 }
        ];

        Assert.True(IsValid(character));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void ValidClassLevel_PassesValidation(int level)
    {
        var character = BuildValidCharacter();
        character.Classes[0].Level = level;

        Assert.True(IsValid(character));
    }

    // ── Ability score validation ──────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void InvalidStrength_FailsValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Strength = score;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Strength"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void InvalidDexterity_FailsValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Dexterity = score;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Dexterity"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void InvalidConstitution_FailsValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Constitution = score;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Constitution"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void InvalidIntelligence_FailsValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Intelligence = score;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Intelligence"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void InvalidWisdom_FailsValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Wisdom = score;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Wisdom"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void InvalidCharisma_FailsValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Charisma = score;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "Charisma"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void ValidAbilityScore_PassesValidation(int score)
    {
        var character = BuildValidCharacter();
        character.Abilities.Strength = score;
        character.Abilities.Dexterity = score;
        character.Abilities.Constitution = score;
        character.Abilities.Intelligence = score;
        character.Abilities.Wisdom = score;
        character.Abilities.Charisma = score;

        Assert.True(IsValid(character));
    }

    // ── HP validation ─────────────────────────────────────────────────────────

    [Fact]
    public void NegativeMaxHitPoints_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Combat.MaxHitPoints = -1;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "MaxHitPoints"));
    }

    [Fact]
    public void NegativeCurrentHitPoints_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Combat.CurrentHitPoints = -1;

        Assert.False(IsValid(character));
        Assert.True(HasErrorFor(character, "CurrentHitPoints"));
    }

    [Fact]
    public void ZeroHitPoints_PassesValidation()
    {
        var character = BuildValidCharacter();
        character.Combat.MaxHitPoints = 0;
        character.Combat.CurrentHitPoints = 0;

        Assert.True(IsValid(character));
    }

    // ── Spell slot validation ─────────────────────────────────────────────────

    [Fact]
    public void SpellSlotLevelZero_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Spells = new SpellcastingInfo
        {
            SpellSlots = [new SpellSlot { Level = 0, Total = 2 }]
        };

        Assert.False(IsValid(character));
    }

    [Fact]
    public void SpellSlotLevelTen_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Spells = new SpellcastingInfo
        {
            SpellSlots = [new SpellSlot { Level = 10, Total = 1 }]
        };

        Assert.False(IsValid(character));
    }

    [Fact]
    public void ValidSpellSlots_PassesValidation()
    {
        var character = BuildValidCharacter();
        character.Spells = new SpellcastingInfo
        {
            SpellcastingAbility = "Wisdom",
            SpellSaveDc = 14,
            SpellAttackBonus = 6,
            SpellSlots =
            [
                new SpellSlot { Level = 1, Total = 4, Used = 0 },
                new SpellSlot { Level = 2, Total = 3, Used = 1 }
            ]
        };

        Assert.True(IsValid(character));
    }

    // ── Resource validation ───────────────────────────────────────────────────

    [Fact]
    public void ResourceWithEmptyName_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Resources.Add(new Resource { Name = string.Empty, Current = 1, Maximum = 3 });

        Assert.False(IsValid(character));
    }

    [Fact]
    public void ResourceWithNegativeMaximum_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Resources.Add(new Resource { Name = "Ki", Current = 0, Maximum = -1 });

        Assert.False(IsValid(character));
    }

    // ── Inventory validation ──────────────────────────────────────────────────

    [Fact]
    public void InventoryItemWithEmptyName_FailsValidation()
    {
        var character = BuildValidCharacter();
        character.Inventory.Add(new InventoryItem { Name = string.Empty, Quantity = 1 });

        Assert.False(IsValid(character));
    }
}
