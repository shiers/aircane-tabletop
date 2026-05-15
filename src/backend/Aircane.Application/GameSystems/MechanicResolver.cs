using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Stateless mechanic resolver that handles dice rolling for all supported convention types.
/// Uses a pluggable IRandomSource for testability.
/// </summary>
public class MechanicResolver : IMechanicResolver
{
    private const int MaxExplodingRerolls = 100;

    /// <inheritdoc />
    public DiceParseResult ParseExpression(string formula, DiceConvention convention)
    {
        return DiceExpressionParser.Parse(formula);
    }

    /// <inheritdoc />
    public RollResolution Roll(DiceExpressionNode expression, DiceConvention convention, IRandomSource random)
    {
        return expression switch
        {
            StandardDiceExpression standard => RollStandard(standard, convention, random),
            PoolDiceExpression pool => RollPool(pool, random),
            ExplodingDiceExpression exploding => RollExploding(exploding, convention, random),
            FudgeDiceExpression fudge => RollFudge(fudge, random),
            _ => throw new ArgumentException($"Unsupported dice expression type: {expression.GetType().Name}")
        };
    }

    private RollResolution RollStandard(StandardDiceExpression expr, DiceConvention convention, IRandomSource random)
    {
        // Handle step dice: convention may override the die size
        var sides = expr.Sides;

        // Handle percentile: a standard 1d100 roll
        // (percentile is just a standard roll with 100 sides, no special logic needed)

        // Roll all dice
        var rawResults = new List<int>(expr.Count);
        for (var i = 0; i < expr.Count; i++)
        {
            rawResults.Add(random.Next(1, sides + 1));
        }

        // Apply keep/drop
        var keptResults = ApplyKeepDrop(rawResults, expr.KeepDrop);

        var total = keptResults.Sum() + expr.Modifier;

        // Determine outcome tier for threshold band systems (PbtA)
        string? outcomeTier = null;
        if (convention.Type == DiceConventionType.FixedDiceThreshold)
        {
            outcomeTier = DetermineThresholdBandTier(total);
        }

        return new RollResolution
        {
            RawResults = rawResults,
            KeptResults = keptResults,
            Modifier = expr.Modifier,
            Total = total,
            OutcomeTier = outcomeTier
        };
    }

    private RollResolution RollPool(PoolDiceExpression expr, IRandomSource random)
    {
        var rawResults = new List<int>(expr.Count);
        for (var i = 0; i < expr.Count; i++)
        {
            rawResults.Add(random.Next(1, expr.Sides + 1));
        }

        // Count successes based on comparison operator
        var successCount = expr.Comparison switch
        {
            ">=" => rawResults.Count(r => r >= expr.SuccessThreshold),
            ">" => rawResults.Count(r => r > expr.SuccessThreshold),
            _ => rawResults.Count(r => r >= expr.SuccessThreshold)
        };

        return new RollResolution
        {
            RawResults = rawResults,
            KeptResults = rawResults,
            Modifier = 0,
            Total = successCount,
            SuccessCount = successCount
        };
    }

    private RollResolution RollExploding(ExplodingDiceExpression expr, DiceConvention convention, IRandomSource random)
    {
        // Determine the explosion threshold: explicit or max value of die
        var explodeThreshold = expr.ExplodeThreshold ?? expr.Sides;

        // Use convention's explode threshold if set and expression doesn't override
        if (expr.ExplodeThreshold is null && convention.ExplodeThreshold.HasValue)
        {
            explodeThreshold = convention.ExplodeThreshold.Value;
        }

        var rawResults = new List<int>(expr.Count);
        var explodedResults = new List<int>();
        var totalRerolls = 0;

        // Roll initial dice
        for (var i = 0; i < expr.Count; i++)
        {
            rawResults.Add(random.Next(1, expr.Sides + 1));
        }

        // Process explosions
        var diceToExplode = rawResults.Where(r => r >= explodeThreshold).ToList();
        while (diceToExplode.Count > 0 && totalRerolls < MaxExplodingRerolls)
        {
            var newRolls = new List<int>();
            foreach (var _ in diceToExplode)
            {
                if (totalRerolls >= MaxExplodingRerolls)
                    break;

                var reroll = random.Next(1, expr.Sides + 1);
                explodedResults.Add(reroll);
                newRolls.Add(reroll);
                totalRerolls++;
            }

            diceToExplode = newRolls.Where(r => r >= explodeThreshold).ToList();
        }

        var allResults = rawResults.Concat(explodedResults).ToList();
        var total = allResults.Sum();

        return new RollResolution
        {
            RawResults = rawResults,
            KeptResults = allResults,
            Modifier = 0,
            Total = total,
            ExplodedResults = explodedResults.Count > 0 ? explodedResults : null
        };
    }

    private RollResolution RollFudge(FudgeDiceExpression expr, IRandomSource random)
    {
        var rawResults = new List<int>(expr.Count);
        for (var i = 0; i < expr.Count; i++)
        {
            // Fudge dice: -1, 0, +1
            rawResults.Add(random.Next(0, 3) - 1);
        }

        var total = rawResults.Sum() + expr.Modifier;

        return new RollResolution
        {
            RawResults = rawResults,
            KeptResults = rawResults,
            Modifier = expr.Modifier,
            Total = total
        };
    }

    /// <summary>
    /// Applies keep/drop directives to a list of dice results.
    /// </summary>
    private static List<int> ApplyKeepDrop(List<int> results, KeepDropDirective? keepDrop)
    {
        if (keepDrop is null)
            return results;

        return keepDrop.Type switch
        {
            KeepDropType.KeepHighest => results.OrderByDescending(r => r).Take(keepDrop.Amount).ToList(),
            KeepDropType.KeepLowest => results.OrderBy(r => r).Take(keepDrop.Amount).ToList(),
            KeepDropType.DropHighest => results.OrderByDescending(r => r).Skip(keepDrop.Amount).ToList(),
            KeepDropType.DropLowest => results.OrderBy(r => r).Skip(keepDrop.Amount).ToList(),
            _ => results
        };
    }

    /// <inheritdoc />
    public ResolutionOutcome Resolve(RollResolution roll, ResolutionRule rule, CharacterState? characterState = null)
    {
        return rule.Type switch
        {
            ResolutionRuleType.TargetNumber => ResolveTargetNumber(roll, rule, characterState),
            ResolutionRuleType.Opposed => ResolveOpposed(roll, rule, characterState),
            ResolutionRuleType.DegreesOfSuccess => ResolveDegreesOfSuccess(roll, rule),
            ResolutionRuleType.Margin => ResolveMargin(roll, rule, characterState),
            ResolutionRuleType.ThresholdBands => ResolveThresholdBands(roll),
            _ => new ResolutionOutcome
            {
                Outcome = "Error",
                IsSuccess = false,
                Error = $"Unsupported resolution rule type: {rule.Type}"
            }
        };
    }

    private static ResolutionOutcome ResolveTargetNumber(RollResolution roll, ResolutionRule rule, CharacterState? characterState)
    {
        // Check for automatic critical success/failure first (natural roll)
        var naturalRoll = roll.RawResults.Count > 0 ? roll.RawResults[0] : 0;

        if (rule.CriticalSuccess is not null && naturalRoll == rule.CriticalSuccess.NaturalRoll)
        {
            return new ResolutionOutcome
            {
                Outcome = "Critical Success",
                IsSuccess = true,
                IsCritical = true
            };
        }

        if (rule.CriticalFailure is not null && naturalRoll == rule.CriticalFailure.NaturalRoll)
        {
            return new ResolutionOutcome
            {
                Outcome = "Critical Failure",
                IsSuccess = false,
                IsCritical = true
            };
        }

        // Look up target number from character state
        if (rule.TargetSource is null)
        {
            return new ResolutionOutcome
            {
                Outcome = "Error",
                IsSuccess = false,
                Error = "Resolution rule has no target source defined"
            };
        }

        if (characterState is null || !characterState.Attributes.TryGetValue(rule.TargetSource, out var target))
        {
            return new ResolutionOutcome
            {
                Outcome = "Error",
                IsSuccess = false,
                Error = $"Missing attribute '{rule.TargetSource}' in character state"
            };
        }

        // Compare roll total against target using the comparison operator
        var success = rule.Comparison switch
        {
            ">=" => roll.Total >= target,
            ">" => roll.Total > target,
            "<=" => roll.Total <= target,
            "<" => roll.Total < target,
            "=" or "==" => roll.Total == target,
            _ => roll.Total >= target // Default to >=
        };

        return new ResolutionOutcome
        {
            Outcome = success ? "Success" : "Failure",
            IsSuccess = success
        };
    }

    private static ResolutionOutcome ResolveOpposed(RollResolution roll, ResolutionRule rule, CharacterState? characterState)
    {
        // For opposed rolls, the defender's total is passed via CharacterState.Attributes["defender_total"]
        if (characterState is null || !characterState.Attributes.TryGetValue("defender_total", out var defenderTotal))
        {
            return new ResolutionOutcome
            {
                Outcome = "Error",
                IsSuccess = false,
                Error = "Missing attribute 'defender_total' in character state for opposed roll"
            };
        }

        var attackerTotal = roll.Total;

        if (attackerTotal > defenderTotal)
        {
            return new ResolutionOutcome
            {
                Outcome = "Attacker Wins",
                IsSuccess = true
            };
        }

        if (defenderTotal > attackerTotal)
        {
            return new ResolutionOutcome
            {
                Outcome = "Defender Wins",
                IsSuccess = false
            };
        }

        // Tie - apply tie-breaking rule
        return rule.TieBreaker?.ToLowerInvariant() switch
        {
            "attacker_wins" => new ResolutionOutcome
            {
                Outcome = "Attacker Wins",
                IsSuccess = true
            },
            "defender_wins" => new ResolutionOutcome
            {
                Outcome = "Defender Wins",
                IsSuccess = false
            },
            _ => new ResolutionOutcome
            {
                Outcome = "Tie",
                IsSuccess = false
            }
        };
    }

    private static ResolutionOutcome ResolveDegreesOfSuccess(RollResolution roll, ResolutionRule rule)
    {
        if (rule.DegreesOfSuccess is null || rule.DegreesOfSuccess.Count == 0)
        {
            return new ResolutionOutcome
            {
                Outcome = "Failure",
                IsSuccess = false
            };
        }

        // Check each degree threshold in order
        foreach (var degree in rule.DegreesOfSuccess)
        {
            var meetsMin = degree.MinValue is null || roll.Total >= degree.MinValue;
            var meetsMax = degree.MaxValue is null || roll.Total <= degree.MaxValue;

            if (meetsMin && meetsMax)
            {
                var isSuccess = degree.Name.Contains("Success", StringComparison.OrdinalIgnoreCase);
                var isCritical = degree.Name.Contains("Critical", StringComparison.OrdinalIgnoreCase);

                return new ResolutionOutcome
                {
                    Outcome = degree.Name,
                    IsSuccess = isSuccess,
                    IsCritical = isCritical
                };
            }
        }

        // No threshold matched
        return new ResolutionOutcome
        {
            Outcome = "Failure",
            IsSuccess = false
        };
    }

    private static ResolutionOutcome ResolveMargin(RollResolution roll, ResolutionRule rule, CharacterState? characterState)
    {
        // Look up target number from character state
        if (rule.TargetSource is null)
        {
            return new ResolutionOutcome
            {
                Outcome = "Error",
                IsSuccess = false,
                Error = "Resolution rule has no target source defined"
            };
        }

        if (characterState is null || !characterState.Attributes.TryGetValue(rule.TargetSource, out var target))
        {
            return new ResolutionOutcome
            {
                Outcome = "Error",
                IsSuccess = false,
                Error = $"Missing attribute '{rule.TargetSource}' in character state"
            };
        }

        var margin = roll.Total - target;
        var success = margin > 0;

        return new ResolutionOutcome
        {
            Outcome = success ? "Success" : "Failure",
            IsSuccess = success,
            Margin = margin
        };
    }

    private static ResolutionOutcome ResolveThresholdBands(RollResolution roll)
    {
        // For threshold band systems, the outcome tier is already determined during rolling
        if (roll.OutcomeTier is not null)
        {
            // Determine success based on the tier name
            var isSuccess = !roll.OutcomeTier.Equals("Miss", StringComparison.OrdinalIgnoreCase);

            return new ResolutionOutcome
            {
                Outcome = roll.OutcomeTier,
                IsSuccess = isSuccess
            };
        }

        // Fallback: if no outcome tier was set, use default PbtA bands
        var tier = DetermineThresholdBandTier(roll.Total);
        return new ResolutionOutcome
        {
            Outcome = tier,
            IsSuccess = tier != "Miss"
        };
    }

    /// <summary>
    /// Determines the outcome tier for PbtA-style threshold band systems.
    /// Standard PbtA bands: 6- = Miss, 7-9 = Weak Hit, 10+ = Strong Hit.
    /// </summary>
    private static string DetermineThresholdBandTier(int total)
    {
        return total switch
        {
            <= 6 => "Miss",
            <= 9 => "Weak Hit",
            _ => "Strong Hit"
        };
    }
}
