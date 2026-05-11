using Aircane.Application.GameSystems;
using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class ResolutionRuleEngineTests
{
    private readonly MechanicResolver _resolver = new();

    #region Target Number Tests

    [Fact]
    public void Resolve_TargetNumber_Success_WhenRollMeetsDC()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 3,
            Total = 18
        };
        var rule = new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.IsCritical);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void Resolve_TargetNumber_Success_WhenRollExactlyEqualsDC()
    {
        var roll = new RollResolution
        {
            RawResults = [12],
            KeptResults = [12],
            Modifier = 3,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_TargetNumber_Failure_WhenRollBelowDC()
    {
        var roll = new RollResolution
        {
            RawResults = [5],
            KeptResults = [5],
            Modifier = 2,
            Total = 7
        };
        var rule = new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_TargetNumber_CriticalSuccess_OnNatural20()
    {
        var roll = new RollResolution
        {
            RawResults = [20],
            KeptResults = [20],
            Modifier = 5,
            Total = 25
        };
        var rule = new ResolutionRule
        {
            Name = "attackRoll",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "ac",
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["ac"] = 18 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Critical Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_TargetNumber_CriticalFailure_OnNatural1()
    {
        var roll = new RollResolution
        {
            RawResults = [1],
            KeptResults = [1],
            Modifier = 10,
            Total = 11
        };
        var rule = new ResolutionRule
        {
            Name = "attackRoll",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "ac",
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["ac"] = 10 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Critical Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_TargetNumber_Error_WhenAttributeMissing()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 3,
            Total = 18
        };
        var rule = new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["ac"] = 15 } // "dc" is missing
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.NotNull(outcome.Error);
        Assert.Contains("dc", outcome.Error);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_TargetNumber_Error_WhenCharacterStateIsNull()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 3,
            Total = 18
        };
        var rule = new ResolutionRule
        {
            Name = "abilityCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        };

        var outcome = _resolver.Resolve(roll, rule, null);

        Assert.NotNull(outcome.Error);
        Assert.Contains("dc", outcome.Error);
    }

    [Fact]
    public void Resolve_TargetNumber_StrictGreaterThan()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "check",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        // Exactly equal should fail with strict >
        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_TargetNumber_LessThanOrEqual()
    {
        // For systems where rolling under is success (e.g., BRP/Call of Cthulhu)
        var roll = new RollResolution
        {
            RawResults = [35],
            KeptResults = [35],
            Modifier = 0,
            Total = 35
        };
        var rule = new ResolutionRule
        {
            Name = "skillCheck",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = "<=",
            TargetSource = "skill"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["skill"] = 50 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    #endregion

    #region Opposed Roll Tests

    [Fact]
    public void Resolve_Opposed_AttackerWins_WhenHigherTotal()
    {
        var roll = new RollResolution
        {
            RawResults = [18],
            KeptResults = [18],
            Modifier = 3,
            Total = 21
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = "attacker_wins"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Attacker Wins", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_Opposed_DefenderWins_WhenHigherTotal()
    {
        var roll = new RollResolution
        {
            RawResults = [8],
            KeptResults = [8],
            Modifier = 2,
            Total = 10
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = "attacker_wins"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = 18 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Defender Wins", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_Opposed_Tie_AttackerWins_WhenTieBreakerIsAttacker()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = "attacker_wins"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Attacker Wins", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_Opposed_Tie_DefenderWins_WhenTieBreakerIsDefender()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = "defender_wins"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Defender Wins", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_Opposed_Tie_ReturnsReroll_WhenTieBreakerIsReroll()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = "reroll"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Tie", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_Opposed_Tie_NoTieBreaker_ReturnsTie()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = null
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["defender_total"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Tie", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_Opposed_Error_WhenDefenderTotalMissing()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "opposed",
            Type = ResolutionRuleType.Opposed,
            Roll = "primary",
            TieBreaker = "attacker_wins"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["ac"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.NotNull(outcome.Error);
        Assert.Contains("defender_total", outcome.Error);
    }

    #endregion

    #region Degrees of Success Tests

    [Fact]
    public void Resolve_DegreesOfSuccess_CriticalSuccess()
    {
        var roll = new RollResolution
        {
            RawResults = [20],
            KeptResults = [20],
            Modifier = 10,
            Total = 30
        };
        var rule = new ResolutionRule
        {
            Name = "pf2eCheck",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess =
            [
                new DegreeThreshold { Name = "Critical Success", MinValue = 25, MaxValue = null },
                new DegreeThreshold { Name = "Success", MinValue = 15, MaxValue = 24 },
                new DegreeThreshold { Name = "Failure", MinValue = 5, MaxValue = 14 },
                new DegreeThreshold { Name = "Critical Failure", MinValue = null, MaxValue = 4 }
            ]
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Critical Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_DegreesOfSuccess_Success()
    {
        var roll = new RollResolution
        {
            RawResults = [14],
            KeptResults = [14],
            Modifier = 5,
            Total = 19
        };
        var rule = new ResolutionRule
        {
            Name = "pf2eCheck",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess =
            [
                new DegreeThreshold { Name = "Critical Success", MinValue = 25, MaxValue = null },
                new DegreeThreshold { Name = "Success", MinValue = 15, MaxValue = 24 },
                new DegreeThreshold { Name = "Failure", MinValue = 5, MaxValue = 14 },
                new DegreeThreshold { Name = "Critical Failure", MinValue = null, MaxValue = 4 }
            ]
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_DegreesOfSuccess_Failure()
    {
        var roll = new RollResolution
        {
            RawResults = [5],
            KeptResults = [5],
            Modifier = 3,
            Total = 8
        };
        var rule = new ResolutionRule
        {
            Name = "pf2eCheck",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess =
            [
                new DegreeThreshold { Name = "Critical Success", MinValue = 25, MaxValue = null },
                new DegreeThreshold { Name = "Success", MinValue = 15, MaxValue = 24 },
                new DegreeThreshold { Name = "Failure", MinValue = 5, MaxValue = 14 },
                new DegreeThreshold { Name = "Critical Failure", MinValue = null, MaxValue = 4 }
            ]
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_DegreesOfSuccess_CriticalFailure()
    {
        var roll = new RollResolution
        {
            RawResults = [1],
            KeptResults = [1],
            Modifier = 2,
            Total = 3
        };
        var rule = new ResolutionRule
        {
            Name = "pf2eCheck",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess =
            [
                new DegreeThreshold { Name = "Critical Success", MinValue = 25, MaxValue = null },
                new DegreeThreshold { Name = "Success", MinValue = 15, MaxValue = 24 },
                new DegreeThreshold { Name = "Failure", MinValue = 5, MaxValue = 14 },
                new DegreeThreshold { Name = "Critical Failure", MinValue = null, MaxValue = 4 }
            ]
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Critical Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_DegreesOfSuccess_NoMatch_ReturnsFailure()
    {
        var roll = new RollResolution
        {
            RawResults = [10],
            KeptResults = [10],
            Modifier = 0,
            Total = 10
        };
        // Gaps in thresholds — total 10 doesn't match any band
        var rule = new ResolutionRule
        {
            Name = "gappyCheck",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess =
            [
                new DegreeThreshold { Name = "Critical Success", MinValue = 20, MaxValue = null },
                new DegreeThreshold { Name = "Success", MinValue = 15, MaxValue = 19 },
                new DegreeThreshold { Name = "Critical Failure", MinValue = null, MaxValue = 5 }
            ]
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_DegreesOfSuccess_NullThresholds_ReturnsFailure()
    {
        var roll = new RollResolution
        {
            RawResults = [10],
            KeptResults = [10],
            Modifier = 0,
            Total = 10
        };
        var rule = new ResolutionRule
        {
            Name = "emptyCheck",
            Type = ResolutionRuleType.DegreesOfSuccess,
            Roll = "primary",
            DegreesOfSuccess = null
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    #endregion

    #region Margin Tests

    [Fact]
    public void Resolve_Margin_Success_WithPositiveMargin()
    {
        var roll = new RollResolution
        {
            RawResults = [16],
            KeptResults = [16],
            Modifier = 4,
            Total = 20
        };
        var rule = new ResolutionRule
        {
            Name = "marginCheck",
            Type = ResolutionRuleType.Margin,
            Roll = "primary",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Success", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(5, outcome.Margin);
    }

    [Fact]
    public void Resolve_Margin_Failure_WithNegativeMargin()
    {
        var roll = new RollResolution
        {
            RawResults = [5],
            KeptResults = [5],
            Modifier = 2,
            Total = 7
        };
        var rule = new ResolutionRule
        {
            Name = "marginCheck",
            Type = ResolutionRuleType.Margin,
            Roll = "primary",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(-8, outcome.Margin);
    }

    [Fact]
    public void Resolve_Margin_Failure_WhenMarginIsZero()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "marginCheck",
            Type = ResolutionRuleType.Margin,
            Roll = "primary",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 15 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Failure", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(0, outcome.Margin);
    }

    [Fact]
    public void Resolve_Margin_Error_WhenAttributeMissing()
    {
        var roll = new RollResolution
        {
            RawResults = [15],
            KeptResults = [15],
            Modifier = 0,
            Total = 15
        };
        var rule = new ResolutionRule
        {
            Name = "marginCheck",
            Type = ResolutionRuleType.Margin,
            Roll = "primary",
            TargetSource = "dc"
        };

        var outcome = _resolver.Resolve(roll, rule, null);

        Assert.NotNull(outcome.Error);
        Assert.Contains("dc", outcome.Error);
    }

    #endregion

    #region Threshold Bands Tests

    [Fact]
    public void Resolve_ThresholdBands_PassesThroughOutcomeTier()
    {
        var roll = new RollResolution
        {
            RawResults = [5, 4],
            KeptResults = [5, 4],
            Modifier = 2,
            Total = 11,
            OutcomeTier = "Strong Hit"
        };
        var rule = new ResolutionRule
        {
            Name = "pbtaMove",
            Type = ResolutionRuleType.ThresholdBands,
            Roll = "primary"
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Strong Hit", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_ThresholdBands_Miss_IsNotSuccess()
    {
        var roll = new RollResolution
        {
            RawResults = [2, 1],
            KeptResults = [2, 1],
            Modifier = 0,
            Total = 3,
            OutcomeTier = "Miss"
        };
        var rule = new ResolutionRule
        {
            Name = "pbtaMove",
            Type = ResolutionRuleType.ThresholdBands,
            Roll = "primary"
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Miss", outcome.Outcome);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_ThresholdBands_WeakHit_IsSuccess()
    {
        var roll = new RollResolution
        {
            RawResults = [4, 3],
            KeptResults = [4, 3],
            Modifier = 1,
            Total = 8,
            OutcomeTier = "Weak Hit"
        };
        var rule = new ResolutionRule
        {
            Name = "pbtaMove",
            Type = ResolutionRuleType.ThresholdBands,
            Roll = "primary"
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Weak Hit", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Resolve_ThresholdBands_FallbackWhenNoOutcomeTier()
    {
        // When OutcomeTier is null, use default PbtA bands
        var roll = new RollResolution
        {
            RawResults = [5, 5],
            KeptResults = [5, 5],
            Modifier = 0,
            Total = 10,
            OutcomeTier = null
        };
        var rule = new ResolutionRule
        {
            Name = "pbtaMove",
            Type = ResolutionRuleType.ThresholdBands,
            Roll = "primary"
        };

        var outcome = _resolver.Resolve(roll, rule);

        Assert.Equal("Strong Hit", outcome.Outcome);
        Assert.True(outcome.IsSuccess);
    }

    #endregion

    #region Single Outcome Classification Tests

    [Fact]
    public void Resolve_AlwaysReturnsExactlyOneOutcome()
    {
        // Verify that the outcome is never empty
        var roll = new RollResolution
        {
            RawResults = [10],
            KeptResults = [10],
            Modifier = 0,
            Total = 10
        };
        var rule = new ResolutionRule
        {
            Name = "check",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "dc"
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["dc"] = 10 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.NotNull(outcome);
        Assert.NotEmpty(outcome.Outcome);
    }

    [Fact]
    public void Resolve_CriticalSuccess_TakesPrecedence_OverNormalSuccess()
    {
        // Natural 20 with a high total that would also succeed normally
        var roll = new RollResolution
        {
            RawResults = [20],
            KeptResults = [20],
            Modifier = 5,
            Total = 25
        };
        var rule = new ResolutionRule
        {
            Name = "attackRoll",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "ac",
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 }
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["ac"] = 10 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        // Should be Critical Success, not just Success
        Assert.Equal("Critical Success", outcome.Outcome);
        Assert.True(outcome.IsCritical);
    }

    [Fact]
    public void Resolve_CriticalFailure_TakesPrecedence_EvenIfTotalWouldSucceed()
    {
        // Natural 1 but with high modifier that would normally succeed
        var roll = new RollResolution
        {
            RawResults = [1],
            KeptResults = [1],
            Modifier = 15,
            Total = 16
        };
        var rule = new ResolutionRule
        {
            Name = "attackRoll",
            Type = ResolutionRuleType.TargetNumber,
            Roll = "primary",
            Comparison = ">=",
            TargetSource = "ac",
            CriticalSuccess = new CriticalCondition { NaturalRoll = 20 },
            CriticalFailure = new CriticalCondition { NaturalRoll = 1 }
        };
        var state = new CharacterState
        {
            Attributes = new Dictionary<string, int> { ["ac"] = 10 }
        };

        var outcome = _resolver.Resolve(roll, rule, state);

        Assert.Equal("Critical Failure", outcome.Outcome);
        Assert.True(outcome.IsCritical);
        Assert.False(outcome.IsSuccess);
    }

    #endregion
}
