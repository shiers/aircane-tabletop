using Aircane.Application.Validation;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems.Seeds;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for the built-in Game System Definition seeds.
/// Verifies that seed data is structurally valid and complete.
/// </summary>
public class GameSystemSeedTests
{
    // ── D&D 5e 2014 Seed Tests ────────────────────────────────────────────────

    [Fact]
    public void DnD5e2014Seed_HasCorrectIdentifier()
    {
        var definition = DnD5e2014Seed.Create();
        Assert.Equal("dnd-5e-2014", definition.Identifier);
    }

    [Fact]
    public void DnD5e2014Seed_HasCorrectMetadata()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.Equal("Dungeons & Dragons 5th Edition (2014)", definition.Name);
        Assert.Equal("1.0.0", definition.Version);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal("built-in", definition.License);
        Assert.Equal("Wizards of the Coast", definition.Publisher);
        Assert.Equal("fantasy", definition.Genre);
        Assert.True(definition.IsBuiltIn);
        Assert.True(definition.IsActive);
    }

    [Fact]
    public void DnD5e2014Seed_HasDeterministicId()
    {
        var definition = DnD5e2014Seed.Create();
        Assert.Equal(DnD5e2014Seed.DefinitionId, definition.Id);
    }

    [Fact]
    public void DnD5e2014Seed_HasDiceConventions()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.NotEmpty(definition.DiceConventions);
        var primary = definition.DiceConventions.First(c => c.Name == "primary");
        Assert.Equal(DiceConventionType.SingleDieModifier, primary.Type);
        Assert.Equal("d20", primary.Die);
        Assert.NotNull(primary.Advantage);
        Assert.Equal(2, primary.Advantage.Roll);
        Assert.Equal("highest", primary.Advantage.Keep);
        Assert.NotNull(primary.Disadvantage);
        Assert.Equal(2, primary.Disadvantage.Roll);
        Assert.Equal("lowest", primary.Disadvantage.Keep);
    }

    [Fact]
    public void DnD5e2014Seed_HasResolutionRules()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.Equal(3, definition.ResolutionRules.Count);

        var abilityCheck = definition.ResolutionRules.First(r => r.Name == "abilityCheck");
        Assert.Equal(ResolutionRuleType.TargetNumber, abilityCheck.Type);
        Assert.Equal(">=", abilityCheck.Comparison);
        Assert.Equal("dc", abilityCheck.TargetSource);

        var attackRoll = definition.ResolutionRules.First(r => r.Name == "attackRoll");
        Assert.Equal(ResolutionRuleType.TargetNumber, attackRoll.Type);
        Assert.Equal("ac", attackRoll.TargetSource);
        Assert.NotNull(attackRoll.CriticalSuccess);
        Assert.Equal(20, attackRoll.CriticalSuccess.NaturalRoll);
        Assert.NotNull(attackRoll.CriticalFailure);
        Assert.Equal(1, attackRoll.CriticalFailure.NaturalRoll);
    }

    [Fact]
    public void DnD5e2014Seed_HasCharacterSchema()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.NotNull(definition.CharacterSchema);
        Assert.NotEmpty(definition.CharacterSchema.Sections);

        // Verify basic sections exist
        var sectionIds = definition.CharacterSchema.Sections.Select(s => s.Id).ToList();
        Assert.Contains("basics", sectionIds);
        Assert.Contains("abilities", sectionIds);
        Assert.Contains("combat", sectionIds);
        Assert.Contains("spells", sectionIds);
    }

    [Fact]
    public void DnD5e2014Seed_HasAbilityScoresWithCalculatedModifiers()
    {
        var definition = DnD5e2014Seed.Create();

        var abilities = definition.CharacterSchema!.Sections.First(s => s.Id == "abilities");
        var strMod = abilities.Fields.First(f => f.Id == "str_mod");
        Assert.Equal(CharacterFieldType.Calculated, strMod.Type);
        Assert.Equal("floor((str - 10) / 2)", strMod.Formula);
    }

    [Fact]
    public void DnD5e2014Seed_Has14StandardConditions()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.Equal(14, definition.ConditionSet.Count);

        var expectedConditions = new[]
        {
            "Blinded", "Charmed", "Deafened", "Frightened", "Grappled",
            "Incapacitated", "Invisible", "Paralyzed", "Petrified",
            "Poisoned", "Prone", "Restrained", "Stunned", "Unconscious"
        };

        foreach (var expected in expectedConditions)
        {
            Assert.Contains(definition.ConditionSet, c => c.Name == expected);
        }
    }

    [Fact]
    public void DnD5e2014Seed_HasNamedSlotsActionEconomy()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.NotNull(definition.ActionEconomy);
        Assert.Equal(ActionEconomyType.NamedSlots, definition.ActionEconomy.Type);
        Assert.NotNull(definition.ActionEconomy.TurnStructure);

        var slotNames = definition.ActionEconomy.TurnStructure.Slots.Select(s => s.Name).ToList();
        Assert.Contains("action", slotNames);
        Assert.Contains("bonus_action", slotNames);
        Assert.Contains("reaction", slotNames);
        Assert.Contains("movement", slotNames);
        Assert.Contains("free_action", slotNames);
    }

    [Fact]
    public void DnD5e2014Seed_HasXpBudgetEncounterSystem()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.NotNull(definition.EncounterBudget);
        Assert.Equal(EncounterBudgetType.XpBudget, definition.EncounterBudget.Type);
        Assert.Equal(4, definition.EncounterBudget.DifficultyTiers.Count);

        var tierNames = definition.EncounterBudget.DifficultyTiers.Select(t => t.Name).ToList();
        Assert.Contains("Easy", tierNames);
        Assert.Contains("Medium", tierNames);
        Assert.Contains("Hard", tierNames);
        Assert.Contains("Deadly", tierNames);
    }

    [Fact]
    public void DnD5e2014Seed_HasAiGuidance()
    {
        var definition = DnD5e2014Seed.Create();

        Assert.NotNull(definition.AiGuidance);
        Assert.NotNull(definition.AiGuidance.SystemPromptNotes);
        Assert.NotNull(definition.AiGuidance.ToneGuidance);
        Assert.NotNull(definition.AiGuidance.MechanicalNotes);
        Assert.NotEmpty(definition.AiGuidance.CommonMistakes);
        Assert.NotNull(definition.AiGuidance.RollFormatExample);
    }

    // ── Generic Freeform Seed Tests ───────────────────────────────────────────

    [Fact]
    public void GenericFreeformSeed_HasCorrectIdentifier()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Equal("generic-freeform", definition.Identifier);
    }

    [Fact]
    public void GenericFreeformSeed_HasCorrectMetadata()
    {
        var definition = GenericFreeformSeed.Create();

        Assert.Equal("Generic Freeform", definition.Name);
        Assert.Equal("1.0.0", definition.Version);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal("built-in", definition.License);
        Assert.True(definition.IsBuiltIn);
        Assert.True(definition.IsActive);
    }

    [Fact]
    public void GenericFreeformSeed_HasDeterministicId()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Equal(GenericFreeformSeed.DefinitionId, definition.Id);
    }

    [Fact]
    public void GenericFreeformSeed_HasNoDiceConventions()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Empty(definition.DiceConventions);
    }

    [Fact]
    public void GenericFreeformSeed_HasNoResolutionRules()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Empty(definition.ResolutionRules);
    }

    [Fact]
    public void GenericFreeformSeed_HasNoCharacterSchema()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Null(definition.CharacterSchema);
    }

    [Fact]
    public void GenericFreeformSeed_HasNoConditionSet()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Empty(definition.ConditionSet);
    }

    [Fact]
    public void GenericFreeformSeed_HasFreeformActionEconomy()
    {
        var definition = GenericFreeformSeed.Create();

        Assert.NotNull(definition.ActionEconomy);
        Assert.Equal(ActionEconomyType.Freeform, definition.ActionEconomy.Type);
    }

    [Fact]
    public void GenericFreeformSeed_HasNoEncounterBudget()
    {
        var definition = GenericFreeformSeed.Create();
        Assert.Null(definition.EncounterBudget);
    }

    [Fact]
    public void GenericFreeformSeed_HasAiGuidance()
    {
        var definition = GenericFreeformSeed.Create();

        Assert.NotNull(definition.AiGuidance);
        Assert.NotNull(definition.AiGuidance.SystemPromptNotes);
        Assert.Contains("freeform", definition.AiGuidance.SystemPromptNotes, StringComparison.OrdinalIgnoreCase);
    }

    // ── Pathfinder 2e Remaster Seed Tests ─────────────────────────────────────

    [Fact]
    public void Pf2eRemasterSeed_HasCorrectMetadataAndDeterministicId()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        Assert.Equal("pathfinder-2e-remaster", d.Identifier);
        Assert.Equal("Pathfinder Second Edition (Remaster)", d.Name);
        Assert.Equal("built-in", d.License);
        Assert.Equal("Paizo Inc.", d.Publisher);
        Assert.True(d.IsBuiltIn);
        Assert.True(d.IsActive);
        Assert.Equal(Pathfinder2eRemasterSeed.DefinitionId, d.Id);
    }

    [Fact]
    public void Pf2eRemasterSeed_UsesDegreesOfSuccessResolution()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        Assert.All(d.ResolutionRules, r => Assert.Equal(ResolutionRuleType.DegreesOfSuccess, r.Type));

        var check = d.ResolutionRules.First(r => r.Name == "check");
        Assert.NotNull(check.DegreesOfSuccess);
        var degreeNames = check.DegreesOfSuccess!.Select(x => x.Name).ToList();
        Assert.Contains("Critical Success", degreeNames);
        Assert.Contains("Success", degreeNames);
        Assert.Contains("Failure", degreeNames);
        Assert.Contains("Critical Failure", degreeNames);

        // Critical success band is a margin of +10 or more; critical failure -10 or worse.
        Assert.Equal(10, check.DegreesOfSuccess.First(x => x.Name == "Critical Success").MinValue);
        Assert.Equal(-10, check.DegreesOfSuccess.First(x => x.Name == "Critical Failure").MaxValue);
    }

    [Fact]
    public void Pf2eRemasterSeed_UsesThreeActionMultiAttackPenaltyEconomy()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        Assert.NotNull(d.ActionEconomy);
        Assert.Equal(ActionEconomyType.MultiActionPenalty, d.ActionEconomy!.Type);
        Assert.Equal(3, d.ActionEconomy.MaxActions);
        Assert.Equal(-5, d.ActionEconomy.PenaltyIncrement);

        var actionSlot = d.ActionEconomy.TurnStructure!.Slots.First(s => s.Name == "action");
        Assert.Equal(3, actionSlot.Count);
    }

    [Fact]
    public void Pf2eRemasterSeed_UsesCreatureLevelEncounterBudget()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        Assert.NotNull(d.EncounterBudget);
        Assert.Equal(EncounterBudgetType.CreatureLevel, d.EncounterBudget!.Type);
        var tiers = d.EncounterBudget.DifficultyTiers.Select(t => t.Name).ToList();
        Assert.Contains("Trivial", tiers);
        Assert.Contains("Moderate", tiers);
        Assert.Contains("Severe", tiers);
        Assert.Contains("Extreme", tiers);
    }

    [Fact]
    public void Pf2eRemasterSeed_HasPathfinderConditions()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        var names = d.ConditionSet.Select(c => c.Name).ToList();
        // PF2e-specific conditions that differ from D&D 5e.
        Assert.Contains("Dying", names);
        Assert.Contains("Wounded", names);
        Assert.Contains("Off-Guard", names);
        Assert.Contains("Frightened", names);
        Assert.Contains("Slowed", names);
    }

    [Fact]
    public void Pf2eRemasterSeed_HasProficiencyRanksInSchema()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        var prof = d.CharacterSchema!.Sections.First(s => s.Id == "proficiencies");
        var perception = prof.Fields.First(f => f.Id == "perception_rank");
        Assert.Equal(CharacterFieldType.Enum, perception.Type);
        Assert.Contains("Legendary", perception.Options!);
    }

    [Fact]
    public void Pf2eRemasterSeed_AiGuidance_DescribesDegreesOfSuccess()
    {
        var d = Pathfinder2eRemasterSeed.Create();

        Assert.NotNull(d.AiGuidance);
        Assert.Contains("degrees of success", d.AiGuidance!.SystemPromptNotes, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(d.AiGuidance.CommonMistakes);
    }

    // ── All built-in seeds must pass validation so the seeder can persist them ─

    [Theory]
    [MemberData(nameof(BuiltInSeeds))]
    public void BuiltInSeed_PassesValidation(Aircane.Domain.Entities.GameSystems.GameSystemDefinition definition)
    {
        var validator = new GameSystemDefinitionValidator();
        var result = validator.Validate(definition);
        Assert.True(result.IsValid,
            $"Seed '{definition.Identifier}' failed validation: " +
            string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    public static IEnumerable<object[]> BuiltInSeeds() =>
    [
        [DnD5e2014Seed.Create()],
        [GenericFreeformSeed.Create()],
        [Pathfinder2eRemasterSeed.Create()],
    ];
}
