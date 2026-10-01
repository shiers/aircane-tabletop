using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for the EncounterBudgetEngine.
/// Tests budget computation and encounter validation across all budget types.
/// </summary>
public class EncounterBudgetEngineTests
{
    private readonly EncounterBudgetEngine _engine = new();

    // ── ComputeBudget Tests ──────────────────────────────────────────────────

    [Fact]
    public void ComputeBudget_XpBudget_ReturnsCorrectTierBudgets()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 },
                new DifficultyTier { Name = "Medium", Multiplier = 1.0 },
                new DifficultyTier { Name = "Hard", Multiplier = 1.5 },
                new DifficultyTier { Name = "Deadly", Multiplier = 2.0 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [5, 5, 5, 5]
        };

        var result = _engine.ComputeBudget(formula, party);

        // Base = 5 * 4 * 100 = 2000
        Assert.Equal(4, result.TierBudgets.Count);
        Assert.Equal(1000, result.TierBudgets["Easy"]);
        Assert.Equal(2000, result.TierBudgets["Medium"]);
        Assert.Equal(3000, result.TierBudgets["Hard"]);
        Assert.Equal(4000, result.TierBudgets["Deadly"]);
    }

    [Fact]
    public void ComputeBudget_CreatureLevel_ReturnsCorrectTierBudgets()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.CreatureLevel,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Low", Multiplier = 1.0 },
                new DifficultyTier { Name = "Moderate", Multiplier = 1.5 },
                new DifficultyTier { Name = "Severe", Multiplier = 2.0 },
                new DifficultyTier { Name = "Extreme", Multiplier = 3.0 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [3, 3, 3, 3]
        };

        var result = _engine.ComputeBudget(formula, party);

        // Base = 3 * 4 = 12
        Assert.Equal(12.0, result.TierBudgets["Low"]);
        Assert.Equal(18.0, result.TierBudgets["Moderate"]);
        Assert.Equal(24.0, result.TierBudgets["Severe"]);
        Assert.Equal(36.0, result.TierBudgets["Extreme"]);
    }

    [Fact]
    public void ComputeBudget_NarrativeTiers_ReturnsPartyBasedBudgets()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.NarrativeTiers,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Minor", Multiplier = 1.0 },
                new DifficultyTier { Name = "Major", Multiplier = 2.0 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 3,
            CharacterLevels = [1, 1, 1]
        };

        var result = _engine.ComputeBudget(formula, party);

        // Base = 3 (party size)
        Assert.Equal(3.0, result.TierBudgets["Minor"]);
        Assert.Equal(6.0, result.TierBudgets["Major"]);
    }

    [Fact]
    public void ComputeBudget_EmptyParty_ReturnsZeroBudgets()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 0,
            CharacterLevels = []
        };

        var result = _engine.ComputeBudget(formula, party);

        Assert.Equal(0.0, result.TierBudgets["Easy"]);
    }

    [Fact]
    public void ComputeBudget_NoTiersDefined_ReturnsEmptyBudgets()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers = []
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [5, 5, 5, 5]
        };

        var result = _engine.ComputeBudget(formula, party);

        Assert.Empty(result.TierBudgets);
    }

    // ── ValidateEncounter Tests ──────────────────────────────────────────────

    [Fact]
    public void ValidateEncounter_WithinBudget_ReturnsValid()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 },
                new DifficultyTier { Name = "Medium", Multiplier = 1.0 },
                new DifficultyTier { Name = "Hard", Multiplier = 1.5 },
                new DifficultyTier { Name = "Deadly", Multiplier = 2.0 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [5, 5, 5, 5]
        };

        // Total cost = 1500, which is within Hard (3000) and below Deadly (4000)
        var creatures = new List<CreatureThreat>
        {
            new() { Name = "Goblin", Cost = 500 },
            new() { Name = "Goblin", Cost = 500 },
            new() { Name = "Goblin Boss", Cost = 500 }
        };

        var result = _engine.ValidateEncounter(formula, party, creatures);

        Assert.True(result.IsValid);
        Assert.Equal(1500, result.TotalCreatureCost);
        Assert.False(result.ExceedsCeiling);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void ValidateEncounter_ExceedsCeiling_ReturnsFlagged()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 },
                new DifficultyTier { Name = "Medium", Multiplier = 1.0 },
                new DifficultyTier { Name = "Deadly", Multiplier = 2.0 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [5, 5, 5, 5]
        };

        // Base = 2000, Deadly = 4000. Total cost = 5000 exceeds ceiling.
        var creatures = new List<CreatureThreat>
        {
            new() { Name = "Dragon", Cost = 5000 }
        };

        var result = _engine.ValidateEncounter(formula, party, creatures);

        Assert.False(result.IsValid);
        Assert.Equal(5000, result.TotalCreatureCost);
        Assert.True(result.ExceedsCeiling);
        Assert.NotNull(result.Warning);
        Assert.Contains("exceeds", result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateEncounter_NoTiersDefined_ReturnsValidWithoutValidation()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers = []
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [5, 5, 5, 5]
        };

        var creatures = new List<CreatureThreat>
        {
            new() { Name = "Dragon", Cost = 99999 }
        };

        var result = _engine.ValidateEncounter(formula, party, creatures);

        Assert.True(result.IsValid);
        Assert.False(result.ExceedsCeiling);
    }

    [Fact]
    public void ValidateEncounter_MatchesTierCorrectly()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 },
                new DifficultyTier { Name = "Medium", Multiplier = 1.0 },
                new DifficultyTier { Name = "Hard", Multiplier = 1.5 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [5, 5, 5, 5]
        };

        // Base = 2000. Easy = 1000, Medium = 2000, Hard = 3000.
        // Total cost = 800 fits within Easy (1000)
        var creatures = new List<CreatureThreat>
        {
            new() { Name = "Rat", Cost = 400 },
            new() { Name = "Rat", Cost = 400 }
        };

        var result = _engine.ValidateEncounter(formula, party, creatures);

        Assert.True(result.IsValid);
        Assert.Equal("Easy", result.MatchedTier);
    }

    [Fact]
    public void ComputeBudget_IsDeterministic_SameInputsSameOutput()
    {
        var formula = new EncounterBudgetFormula
        {
            Type = EncounterBudgetType.XpBudget,
            DifficultyTiers =
            [
                new DifficultyTier { Name = "Easy", Multiplier = 0.5 },
                new DifficultyTier { Name = "Hard", Multiplier = 1.5 }
            ]
        };

        var party = new PartyComposition
        {
            PartySize = 4,
            CharacterLevels = [3, 4, 5, 6]
        };

        var result1 = _engine.ComputeBudget(formula, party);
        var result2 = _engine.ComputeBudget(formula, party);

        Assert.Equal(result1.TierBudgets["Easy"], result2.TierBudgets["Easy"]);
        Assert.Equal(result1.TierBudgets["Hard"], result2.TierBudgets["Hard"]);
    }
}
