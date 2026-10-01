using Aircane.Application.Characters;
using Xunit;

namespace Aircane.UnitTests.Characters;

/// <summary>
/// Tests for <see cref="CharacterJsonSerializer"/> round-trip serialization
/// and for <see cref="CharacterSchemaValidator"/> validation rules.
/// </summary>
public class CanonicalCharacterSerializationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CanonicalCharacter BuildValidCharacter() => new()
    {
        Identity = new CharacterIdentity
        {
            Name = "Thalindra Moonwhisper",
            RaceOrAncestry = "High Elf",
            Background = "Sage",
            Alignment = "Chaotic Good"
        },
        Classes =
        [
            new CharacterClass { ClassName = "Wizard", Level = 5, Subclass = "School of Evocation", HitDie = 6 }
        ],
        Abilities = new AbilityScores
        {
            Strength = 8,
            Dexterity = 14,
            Constitution = 12,
            Intelligence = 18,
            Wisdom = 13,
            Charisma = 10
        },
        Combat = new CombatStats
        {
            ArmorClass = 13,
            Speed = 30,
            MaxHitPoints = 28,
            CurrentHitPoints = 28,
            ProficiencyBonus = 3
        },
        Spells = new SpellcastingInfo
        {
            SpellcastingAbility = "Intelligence",
            SpellSaveDc = 15,
            SpellAttackBonus = 7,
            SpellSlots =
            [
                new SpellSlot { Level = 1, Total = 4, Used = 0 },
                new SpellSlot { Level = 2, Total = 3, Used = 0 },
                new SpellSlot { Level = 3, Total = 2, Used = 0 }
            ],
            KnownSpells =
            [
                new KnownSpell { Name = "Fireball", Level = 3, School = "Evocation", Prepared = true, Concentration = false },
                new KnownSpell { Name = "Mage Hand", Level = 0, School = "Conjuration" }
            ]
        },
        Features =
        [
            new Feature { Name = "Arcane Recovery", Source = "Wizard 1", Description = "Recover spell slots on short rest." }
        ],
        Inventory =
        [
            new InventoryItem { Name = "Spellbook", Quantity = 1, Weight = 3m, Equipped = true },
            new InventoryItem { Name = "Dagger", Quantity = 2, Weight = 1m, Equipped = false }
        ],
        Resources =
        [
            new Resource { Name = "Arcane Recovery", Current = 1, Maximum = 1, RechargeOn = "Short Rest" }
        ],
        Currency = new Currency { Gold = 50, Silver = 10 },
        Notes = "Searching for the lost library of Candlekeep."
    };

    // ── Serialization round-trip ──────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_ProducesEquivalentCharacter()
    {
        var original = BuildValidCharacter();

        var json = CharacterJsonSerializer.Serialize(original);
        var restored = CharacterJsonSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Identity.Name, restored.Identity.Name);
        Assert.Equal(original.Identity.RaceOrAncestry, restored.Identity.RaceOrAncestry);
        Assert.Equal(original.Classes[0].ClassName, restored.Classes[0].ClassName);
        Assert.Equal(original.Classes[0].Level, restored.Classes[0].Level);
        Assert.Equal(original.Abilities.Intelligence, restored.Abilities.Intelligence);
        Assert.Equal(original.Combat.MaxHitPoints, restored.Combat.MaxHitPoints);
        Assert.Equal(original.Spells!.SpellSaveDc, restored.Spells!.SpellSaveDc);
        Assert.Equal(original.Spells.SpellSlots.Count, restored.Spells.SpellSlots.Count);
        Assert.Equal(original.Spells.KnownSpells.Count, restored.Spells.KnownSpells.Count);
        Assert.Equal(original.Features[0].Name, restored.Features[0].Name);
        Assert.Equal(original.Inventory.Count, restored.Inventory.Count);
        Assert.Equal(original.Resources[0].Name, restored.Resources[0].Name);
        Assert.Equal(original.Currency.Gold, restored.Currency.Gold);
        Assert.Equal(original.Notes, restored.Notes);
    }

    [Fact]
    public void Serialize_ProducesValidJson()
    {
        var character = BuildValidCharacter();

        var json = CharacterJsonSerializer.Serialize(character);

        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.StartsWith("{", json.TrimStart());
    }

    [Fact]
    public void Deserialize_NullInput_ReturnsNull()
    {
        var result = CharacterJsonSerializer.Deserialize(null);
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        var result = CharacterJsonSerializer.Deserialize(string.Empty);
        Assert.Null(result);
    }

    [Fact]
    public void DeserializeOrDefault_NullInput_ReturnsDefaultInstance()
    {
        var result = CharacterJsonSerializer.DeserializeOrDefault(null);
        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Identity.Name);
    }

    [Fact]
    public void TryDeserialize_ValidJson_ReturnsTrueAndCharacter()
    {
        var json = CharacterJsonSerializer.Serialize(BuildValidCharacter());

        var success = CharacterJsonSerializer.TryDeserialize(json, out var character);

        Assert.True(success);
        Assert.NotNull(character);
    }

    [Fact]
    public void TryDeserialize_MalformedJson_ReturnsFalse()
    {
        var success = CharacterJsonSerializer.TryDeserialize("{ not valid json }", out var character);

        Assert.False(success);
        Assert.Null(character);
    }

    // ── CurrentState round-trip ───────────────────────────────────────────────

    [Fact]
    public void SerializeState_ThenDeserializeState_ProducesEquivalentState()
    {
        var state = new CharacterCurrentState
        {
            CurrentHitPoints = 20,
            TemporaryHitPoints = 5,
            DeathSaveSuccesses = 1,
            DeathSaveFailures = 0,
            UsedSpellSlots = new Dictionary<int, int> { [1] = 2, [3] = 1 },
            UsedResources = new Dictionary<string, int> { ["Arcane Recovery"] = 1 },
            Conditions = ["Poisoned"],
            ExhaustionLevel = 1,
            IsConcentrating = true,
            ConcentrationSpell = "Fly"
        };

        var json = CharacterJsonSerializer.SerializeState(state);
        var restored = CharacterJsonSerializer.DeserializeState(json);

        Assert.NotNull(restored);
        Assert.Equal(state.CurrentHitPoints, restored.CurrentHitPoints);
        Assert.Equal(state.TemporaryHitPoints, restored.TemporaryHitPoints);
        Assert.Equal(state.DeathSaveSuccesses, restored.DeathSaveSuccesses);
        Assert.Equal(2, restored.UsedSpellSlots[1]);
        Assert.Equal(1, restored.UsedSpellSlots[3]);
        Assert.Equal(1, restored.UsedResources["Arcane Recovery"]);
        Assert.Contains("Poisoned", restored.Conditions);
        Assert.Equal(state.ExhaustionLevel, restored.ExhaustionLevel);
        Assert.True(restored.IsConcentrating);
        Assert.Equal("Fly", restored.ConcentrationSpell);
    }

    [Fact]
    public void DeserializeStateOrDefault_NullInput_ReturnsDefaultInstance()
    {
        var result = CharacterJsonSerializer.DeserializeStateOrDefault(null);
        Assert.NotNull(result);
        Assert.Equal(0, result.CurrentHitPoints);
        Assert.Empty(result.Conditions);
    }

    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultCanonicalCharacter_HasSensibleDefaults()
    {
        var character = new CanonicalCharacter();

        Assert.Equal(string.Empty, character.Identity.Name);
        Assert.Equal(10, character.Abilities.Strength);
        Assert.Equal(10, character.Abilities.Dexterity);
        Assert.Equal(10, character.Abilities.Constitution);
        Assert.Equal(10, character.Abilities.Intelligence);
        Assert.Equal(10, character.Abilities.Wisdom);
        Assert.Equal(10, character.Abilities.Charisma);
        Assert.Equal(10, character.Combat.ArmorClass);
        Assert.Equal(30, character.Combat.Speed);
        Assert.Equal(2, character.Combat.ProficiencyBonus);
        Assert.Empty(character.Classes);
        Assert.Empty(character.Skills);
        Assert.Empty(character.Attacks);
        Assert.Empty(character.Features);
        Assert.Empty(character.Inventory);
        Assert.Empty(character.Resources);
        Assert.Null(character.Spells);
        Assert.Null(character.Notes);
    }

    [Fact]
    public void AbilityScores_ModifierFor_CalculatesCorrectly()
    {
        Assert.Equal(-1, AbilityScores.ModifierFor(8));
        Assert.Equal(0, AbilityScores.ModifierFor(10));
        Assert.Equal(0, AbilityScores.ModifierFor(11));
        Assert.Equal(1, AbilityScores.ModifierFor(12));
        Assert.Equal(4, AbilityScores.ModifierFor(18));
        Assert.Equal(5, AbilityScores.ModifierFor(20));
        Assert.Equal(10, AbilityScores.ModifierFor(30));
    }

    [Fact]
    public void SpellSlot_Remaining_ReturnsCorrectValue()
    {
        var slot = new SpellSlot { Level = 1, Total = 4, Used = 1 };
        Assert.Equal(3, slot.Remaining);
    }

    [Fact]
    public void SpellSlot_Remaining_NeverGoesNegative()
    {
        var slot = new SpellSlot { Level = 1, Total = 2, Used = 5 };
        Assert.Equal(0, slot.Remaining);
    }
}
