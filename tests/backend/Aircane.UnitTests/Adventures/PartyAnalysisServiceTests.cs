using Aircane.Application.Characters;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class PartyAnalysisServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly PartyAnalysisService _service;

    public PartyAnalysisServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _service = new PartyAnalysisService(_db, NullLogger<PartyAnalysisService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Helper Methods ────────────────────────────────────────────────────────

    private static CanonicalCharacter CreateFighter(int level = 5)
    {
        return new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Thorin" },
            Classes = [new CharacterClass { ClassName = "Fighter", Level = level, HitDie = 10 }],
            Abilities = new AbilityScores
            {
                Strength = 18, Dexterity = 14, Constitution = 16,
                Intelligence = 10, Wisdom = 12, Charisma = 8
            },
            SavingThrows = new SavingThrows { Strength = true, Constitution = true },
            Skills =
            [
                new SkillProficiency { SkillName = "Athletics", ProficiencyLevel = ProficiencyLevel.Proficient },
                new SkillProficiency { SkillName = "Perception", ProficiencyLevel = ProficiencyLevel.Proficient },
            ],
            Combat = new CombatStats
            {
                ArmorClass = 18, MaxHitPoints = 44, CurrentHitPoints = 44,
                Speed = 30, ProficiencyBonus = 3, Initiative = 2
            },
            Attacks =
            [
                new Attack { Name = "Longsword", AttackBonus = 7, Damage = "1d8+4", DamageType = "Slashing", Range = "5 ft." },
                new Attack { Name = "Javelin", AttackBonus = 7, Damage = "1d6+4", DamageType = "Piercing", Range = "30/120 ft.", Properties = ["Thrown"] },
            ],
        };
    }

    private static CanonicalCharacter CreateCleric(int level = 5)
    {
        return new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Elara" },
            Classes = [new CharacterClass { ClassName = "Cleric", Level = level, HitDie = 8 }],
            Abilities = new AbilityScores
            {
                Strength = 14, Dexterity = 10, Constitution = 14,
                Intelligence = 12, Wisdom = 18, Charisma = 13
            },
            SavingThrows = new SavingThrows { Wisdom = true, Charisma = true },
            Skills =
            [
                new SkillProficiency { SkillName = "Medicine", ProficiencyLevel = ProficiencyLevel.Proficient },
                new SkillProficiency { SkillName = "Insight", ProficiencyLevel = ProficiencyLevel.Proficient },
            ],
            Combat = new CombatStats
            {
                ArmorClass = 18, MaxHitPoints = 38, CurrentHitPoints = 38,
                Speed = 30, ProficiencyBonus = 3, Initiative = 0
            },
            Attacks =
            [
                new Attack { Name = "Mace", AttackBonus = 5, Damage = "1d6+2", DamageType = "Bludgeoning", Range = "5 ft." },
            ],
            Spells = new SpellcastingInfo
            {
                SpellcastingAbility = "Wisdom",
                SpellSaveDc = 15,
                SpellAttackBonus = 7,
                SpellSlots =
                [
                    new SpellSlot { Level = 1, Total = 4, Used = 0 },
                    new SpellSlot { Level = 2, Total = 3, Used = 0 },
                    new SpellSlot { Level = 3, Total = 2, Used = 0 },
                ],
                KnownSpells =
                [
                    new KnownSpell { Name = "Sacred Flame", Level = 0, School = "Evocation", Prepared = true, Range = "60 feet" },
                    new KnownSpell { Name = "Cure Wounds", Level = 1, School = "Evocation", Prepared = true, Range = "Touch" },
                    new KnownSpell { Name = "Healing Word", Level = 1, School = "Evocation", Prepared = true, Range = "60 feet" },
                    new KnownSpell { Name = "Guiding Bolt", Level = 1, School = "Evocation", Prepared = true, Range = "120 feet" },
                    new KnownSpell { Name = "Spirit Guardians", Level = 3, School = "Conjuration", Prepared = true, Range = "Self" },
                ],
            },
            Features =
            [
                new Feature { Name = "Channel Divinity", Source = "Cleric 2" },
                new Feature { Name = "Preserve Life", Source = "Life Domain 2" },
            ],
        };
    }

    private static CanonicalCharacter CreateRogue(int level = 5)
    {
        return new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Shadow" },
            Classes = [new CharacterClass { ClassName = "Rogue", Level = level, HitDie = 8 }],
            Abilities = new AbilityScores
            {
                Strength = 10, Dexterity = 18, Constitution = 12,
                Intelligence = 14, Wisdom = 12, Charisma = 14
            },
            SavingThrows = new SavingThrows { Dexterity = true, Intelligence = true },
            Skills =
            [
                new SkillProficiency { SkillName = "Stealth", ProficiencyLevel = ProficiencyLevel.Expert },
                new SkillProficiency { SkillName = "Perception", ProficiencyLevel = ProficiencyLevel.Proficient },
                new SkillProficiency { SkillName = "Investigation", ProficiencyLevel = ProficiencyLevel.Proficient },
                new SkillProficiency { SkillName = "Persuasion", ProficiencyLevel = ProficiencyLevel.Proficient },
            ],
            Combat = new CombatStats
            {
                ArmorClass = 15, MaxHitPoints = 33, CurrentHitPoints = 33,
                Speed = 30, ProficiencyBonus = 3, Initiative = 4
            },
            Attacks =
            [
                new Attack { Name = "Shortsword", AttackBonus = 7, Damage = "1d6+4", DamageType = "Piercing", Range = "5 ft.", Properties = ["Finesse", "Light"] },
                new Attack { Name = "Shortbow", AttackBonus = 7, Damage = "1d6+4", DamageType = "Piercing", Range = "80/320 ft.", Properties = ["Ammunition"] },
            ],
            Features =
            [
                new Feature { Name = "Sneak Attack", Source = "Rogue 1", Description = "3d6 extra damage" },
                new Feature { Name = "Cunning Action", Source = "Rogue 2" },
                new Feature { Name = "Uncanny Dodge", Source = "Rogue 5" },
            ],
        };
    }

    private static CanonicalCharacter CreateWizard(int level = 5)
    {
        return new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Gandalf Jr" },
            Classes = [new CharacterClass { ClassName = "Wizard", Level = level, HitDie = 6 }],
            Abilities = new AbilityScores
            {
                Strength = 8, Dexterity = 14, Constitution = 12,
                Intelligence = 18, Wisdom = 13, Charisma = 10
            },
            SavingThrows = new SavingThrows { Intelligence = true, Wisdom = true },
            Skills =
            [
                new SkillProficiency { SkillName = "Arcana", ProficiencyLevel = ProficiencyLevel.Proficient },
                new SkillProficiency { SkillName = "Investigation", ProficiencyLevel = ProficiencyLevel.Proficient },
            ],
            Combat = new CombatStats
            {
                ArmorClass = 12, MaxHitPoints = 27, CurrentHitPoints = 27,
                Speed = 30, ProficiencyBonus = 3, Initiative = 2
            },
            Attacks =
            [
                new Attack { Name = "Dagger", AttackBonus = 5, Damage = "1d4+2", DamageType = "Piercing", Range = "20/60 ft.", Properties = ["Finesse", "Light", "Thrown"] },
            ],
            Spells = new SpellcastingInfo
            {
                SpellcastingAbility = "Intelligence",
                SpellSaveDc = 15,
                SpellAttackBonus = 7,
                SpellSlots =
                [
                    new SpellSlot { Level = 1, Total = 4, Used = 0 },
                    new SpellSlot { Level = 2, Total = 3, Used = 0 },
                    new SpellSlot { Level = 3, Total = 2, Used = 0 },
                ],
                KnownSpells =
                [
                    new KnownSpell { Name = "Fire Bolt", Level = 0, School = "Evocation", Prepared = true, Range = "120 feet" },
                    new KnownSpell { Name = "Mage Hand", Level = 0, School = "Conjuration", Prepared = true, Range = "30 feet" },
                    new KnownSpell { Name = "Shield", Level = 1, School = "Abjuration", Prepared = true, Range = "Self" },
                    new KnownSpell { Name = "Magic Missile", Level = 1, School = "Evocation", Prepared = true, Range = "120 feet" },
                    new KnownSpell { Name = "Fireball", Level = 3, School = "Evocation", Prepared = true, Range = "150 feet" },
                    new KnownSpell { Name = "Counterspell", Level = 3, School = "Abjuration", Prepared = true, Range = "60 feet" },
                ],
            },
        };
    }

    private async Task<Guid> SeedCharacter(CanonicalCharacter canonical, int? levelOverride = null)
    {
        var level = levelOverride ?? canonical.Classes.Sum(c => c.Level);
        var character = new Character(
            name: canonical.Identity.Name,
            gameSystem: "D&D 5e 2014",
            ruleset: "D&D 5e 2014",
            level: level,
            canonicalJson: CharacterJsonSerializer.Serialize(canonical),
            currentStateJson: "{}");

        _db.Characters.Add(character);
        await _db.SaveChangesAsync();
        return character.Id;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeParty_full_party_returns_correct_size_and_levels()
    {
        var fighterId = await SeedCharacter(CreateFighter(5));
        var clericId = await SeedCharacter(CreateCleric(5));
        var rogueId = await SeedCharacter(CreateRogue(5));
        var wizardId = await SeedCharacter(CreateWizard(5));

        var result = await _service.AnalyzePartyAsync([fighterId, clericId, rogueId, wizardId]);

        Assert.Equal(4, result.PartySize);
        Assert.Equal(5.0, result.AverageLevel);
        Assert.Equal(5, result.MinLevel);
        Assert.Equal(5, result.MaxLevel);
    }

    [Fact]
    public async Task AnalyzeParty_mixed_levels_computes_correct_range()
    {
        var fighterId = await SeedCharacter(CreateFighter(3));
        var wizardId = await SeedCharacter(CreateWizard(7));

        var result = await _service.AnalyzePartyAsync([fighterId, wizardId]);

        Assert.Equal(2, result.PartySize);
        Assert.Equal(5.0, result.AverageLevel);
        Assert.Equal(3, result.MinLevel);
        Assert.Equal(7, result.MaxLevel);
    }

    [Fact]
    public async Task AnalyzeParty_detects_class_distribution()
    {
        var fighter1 = await SeedCharacter(CreateFighter());
        var fighter2 = await SeedCharacter(CreateFighter());
        var clericId = await SeedCharacter(CreateCleric());

        var result = await _service.AnalyzePartyAsync([fighter1, fighter2, clericId]);

        Assert.Equal(2, result.Classes["Fighter"]);
        Assert.Equal(1, result.Classes["Cleric"]);
    }

    [Fact]
    public async Task AnalyzeParty_computes_correct_ac_and_hp()
    {
        var fighterId = await SeedCharacter(CreateFighter()); // AC 18, HP 44
        var wizardId = await SeedCharacter(CreateWizard());   // AC 12, HP 27

        var result = await _service.AnalyzePartyAsync([fighterId, wizardId]);

        Assert.Equal(15.0, result.AverageAC);
        Assert.Equal(35.5, result.AverageHP);
        Assert.Equal(71, result.TotalHP);
    }

    [Fact]
    public async Task AnalyzeParty_detects_healing_from_spells()
    {
        var clericId = await SeedCharacter(CreateCleric());

        var result = await _service.AnalyzePartyAsync([clericId]);

        Assert.True(result.HasHealing);
    }

    [Fact]
    public async Task AnalyzeParty_detects_no_healing_when_absent()
    {
        var fighterId = await SeedCharacter(CreateFighter());
        var wizardId = await SeedCharacter(CreateWizard());

        var result = await _service.AnalyzePartyAsync([fighterId, wizardId]);

        Assert.False(result.HasHealing);
    }

    [Fact]
    public async Task AnalyzeParty_detects_ranged_attacks()
    {
        var rogueId = await SeedCharacter(CreateRogue()); // Has shortbow

        var result = await _service.AnalyzePartyAsync([rogueId]);

        Assert.True(result.HasRangedAttacks);
    }

    [Fact]
    public async Task AnalyzeParty_detects_magic()
    {
        var wizardId = await SeedCharacter(CreateWizard());

        var result = await _service.AnalyzePartyAsync([wizardId]);

        Assert.True(result.HasMagic);
    }

    [Fact]
    public async Task AnalyzeParty_detects_no_magic_for_martial()
    {
        var fighterId = await SeedCharacter(CreateFighter());

        var result = await _service.AnalyzePartyAsync([fighterId]);

        Assert.False(result.HasMagic);
    }

    [Fact]
    public async Task AnalyzeParty_detects_stealth_proficiency()
    {
        var rogueId = await SeedCharacter(CreateRogue());

        var result = await _service.AnalyzePartyAsync([rogueId]);

        Assert.True(result.HasStealth);
    }

    [Fact]
    public async Task AnalyzeParty_detects_perception_proficiency()
    {
        var fighterId = await SeedCharacter(CreateFighter()); // Has perception

        var result = await _service.AnalyzePartyAsync([fighterId]);

        Assert.True(result.HasPerception);
    }

    [Fact]
    public async Task AnalyzeParty_builds_capabilities_list()
    {
        var fighterId = await SeedCharacter(CreateFighter());
        var clericId = await SeedCharacter(CreateCleric());
        var rogueId = await SeedCharacter(CreateRogue());
        var wizardId = await SeedCharacter(CreateWizard());

        var result = await _service.AnalyzePartyAsync([fighterId, clericId, rogueId, wizardId]);

        Assert.Contains(result.Capabilities, c => c.Contains("healing"));
        Assert.Contains(result.Capabilities, c => c.Contains("ranged"));
        Assert.Contains(result.Capabilities, c => c.Contains("spellcasting"));
        Assert.Contains(result.Capabilities, c => c.Contains("stealth"));
        Assert.Contains(result.Capabilities, c => c.Contains("perception"));
    }

    [Fact]
    public async Task AnalyzeParty_builds_weaknesses_for_martial_only_party()
    {
        var fighter = CreateFighter();
        // Remove perception to test weakness detection
        fighter.Skills.Clear();
        fighter.Skills.Add(new SkillProficiency { SkillName = "Athletics", ProficiencyLevel = ProficiencyLevel.Proficient });
        // Remove ranged attacks
        fighter.Attacks.RemoveAll(a => a.Range != "5 ft.");

        var fighterId = await SeedCharacter(fighter);

        var result = await _service.AnalyzePartyAsync([fighterId]);

        Assert.Contains(result.Weaknesses, w => w.Contains("healing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Weaknesses, w => w.Contains("spellcasting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Weaknesses, w => w.Contains("stealth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeParty_throws_for_empty_ids()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AnalyzePartyAsync([]));
    }

    [Fact]
    public async Task AnalyzeParty_throws_when_no_characters_found()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AnalyzePartyAsync([Guid.NewGuid()]));
    }

    [Fact]
    public async Task AnalyzeParty_handles_single_character()
    {
        var clericId = await SeedCharacter(CreateCleric(3));

        var result = await _service.AnalyzePartyAsync([clericId]);

        Assert.Equal(1, result.PartySize);
        Assert.Equal(3.0, result.AverageLevel);
        Assert.Equal(3, result.MinLevel);
        Assert.Equal(3, result.MaxLevel);
    }

    [Fact]
    public async Task AnalyzeParty_detects_healing_from_features()
    {
        var paladin = new CanonicalCharacter
        {
            Identity = new CharacterIdentity { Name = "Paladin" },
            Classes = [new CharacterClass { ClassName = "Paladin", Level = 5, HitDie = 10 }],
            Combat = new CombatStats { ArmorClass = 18, MaxHitPoints = 44, CurrentHitPoints = 44, Speed = 30 },
            Features = [new Feature { Name = "Lay on Hands", Source = "Paladin 1" }],
        };

        var paladinId = await SeedCharacter(paladin);
        var result = await _service.AnalyzePartyAsync([paladinId]);

        Assert.True(result.HasHealing);
    }

    [Fact]
    public async Task AnalyzeParty_detects_ranged_from_cantrips()
    {
        var wizard = CreateWizard(); // Has Fire Bolt cantrip with 120 feet range
        // Remove weapon attacks to isolate cantrip detection
        wizard.Attacks.Clear();

        var wizardId = await SeedCharacter(wizard);
        var result = await _service.AnalyzePartyAsync([wizardId]);

        Assert.True(result.HasRangedAttacks);
    }

    // ── Static Method Tests ───────────────────────────────────────────────────

    [Fact]
    public void HasHealingCapability_returns_true_for_cure_wounds()
    {
        var character = new CanonicalCharacter
        {
            Spells = new SpellcastingInfo
            {
                KnownSpells = [new KnownSpell { Name = "Cure Wounds", Level = 1 }]
            }
        };

        Assert.True(PartyAnalysisService.HasHealingCapability(character));
    }

    [Fact]
    public void HasHealingCapability_returns_false_for_no_healing()
    {
        var character = new CanonicalCharacter
        {
            Spells = new SpellcastingInfo
            {
                KnownSpells = [new KnownSpell { Name = "Fireball", Level = 3 }]
            }
        };

        Assert.False(PartyAnalysisService.HasHealingCapability(character));
    }

    [Fact]
    public void HasRangedAttackCapability_returns_true_for_ranged_weapon()
    {
        var character = new CanonicalCharacter
        {
            Attacks = [new Attack { Name = "Longbow", Range = "150/600 ft.", Properties = ["Ammunition"] }]
        };

        Assert.True(PartyAnalysisService.HasRangedAttackCapability(character));
    }

    [Fact]
    public void HasRangedAttackCapability_returns_false_for_melee_only()
    {
        var character = new CanonicalCharacter
        {
            Attacks = [new Attack { Name = "Greatsword", Range = "5 ft." }]
        };

        Assert.False(PartyAnalysisService.HasRangedAttackCapability(character));
    }

    [Fact]
    public void HasMagicCapability_returns_true_for_spellcaster()
    {
        var character = new CanonicalCharacter
        {
            Spells = new SpellcastingInfo
            {
                KnownSpells = [new KnownSpell { Name = "Magic Missile", Level = 1 }]
            }
        };

        Assert.True(PartyAnalysisService.HasMagicCapability(character));
    }

    [Fact]
    public void HasMagicCapability_returns_false_for_no_spells()
    {
        var character = new CanonicalCharacter();

        Assert.False(PartyAnalysisService.HasMagicCapability(character));
    }

    [Fact]
    public void HasSkillProficiency_returns_true_for_proficient()
    {
        var character = new CanonicalCharacter
        {
            Skills = [new SkillProficiency { SkillName = "Stealth", ProficiencyLevel = ProficiencyLevel.Proficient }]
        };

        Assert.True(PartyAnalysisService.HasSkillProficiency(character, "stealth"));
    }

    [Fact]
    public void HasSkillProficiency_returns_false_for_half_proficient()
    {
        var character = new CanonicalCharacter
        {
            Skills = [new SkillProficiency { SkillName = "Stealth", ProficiencyLevel = ProficiencyLevel.HalfProficient }]
        };

        Assert.False(PartyAnalysisService.HasSkillProficiency(character, "stealth"));
    }
}
