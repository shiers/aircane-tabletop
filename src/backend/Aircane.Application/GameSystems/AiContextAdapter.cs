using System.Text;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Adapts game system definitions into AI-consumable context, formats roll requests
/// using the active convention, and validates AI-proposed actions against the system.
/// </summary>
public class AiContextAdapter : IAiContextAdapter
{
    /// <inheritdoc />
    public string BuildSystemContext(GameSystemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var sb = new StringBuilder();

        sb.AppendLine($"## Game System: {definition.Name}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            sb.AppendLine(definition.Description);
            sb.AppendLine();
        }

        // Dice conventions section
        if (definition.DiceConventions.Count > 0)
        {
            sb.AppendLine("### Dice Conventions");
            foreach (var convention in definition.DiceConventions)
            {
                sb.AppendLine($"- **{convention.Name}** ({GetConventionTypeDescription(convention.Type)}): " +
                              $"{convention.Description ?? GetDefaultConventionDescription(convention)}");
            }
            sb.AppendLine();
        }

        // Resolution rules section
        if (definition.ResolutionRules.Count > 0)
        {
            sb.AppendLine("### Resolution Rules");
            foreach (var rule in definition.ResolutionRules)
            {
                sb.AppendLine($"- **{rule.Name}**: {GetResolutionRuleDescription(rule)}");
            }
            sb.AppendLine();
        }

        // Action economy section
        if (definition.ActionEconomy is not null && definition.ActionEconomy.Type != ActionEconomyType.Freeform)
        {
            sb.AppendLine("### Action Economy");
            sb.AppendLine($"Type: {GetActionEconomyDescription(definition.ActionEconomy)}");
            if (definition.ActionEconomy.TurnStructure?.Slots is { Count: > 0 } slots)
            {
                foreach (var slot in slots)
                {
                    var countDesc = slot.Count == -1 ? "unlimited" : slot.Count.ToString();
                    sb.AppendLine($"- {slot.Label}: {countDesc} per turn");
                }
            }
            sb.AppendLine();
        }

        // Conditions section (brief summary)
        if (definition.ConditionSet.Count > 0)
        {
            sb.AppendLine("### Available Conditions");
            sb.AppendLine($"This system defines {definition.ConditionSet.Count} conditions. " +
                          "Use only these condition names when applying status effects:");
            foreach (var condition in definition.ConditionSet)
            {
                sb.AppendLine($"- {condition.Name}: {condition.Description}");
            }
            sb.AppendLine();
        }

        // AI guidance section
        if (definition.AiGuidance is not null)
        {
            AppendAiGuidance(sb, definition.AiGuidance);
        }

        // Incomplete definition guidance
        if (IsDefinitionIncomplete(definition))
        {
            sb.AppendLine("### Important Note");
            sb.AppendLine("This game system definition is incomplete. When encountering ambiguous " +
                          "mechanical situations, ask the host for clarification rather than assuming " +
                          "conventions from other game systems.");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc />
    public AiRollRequest FormatRollRequest(
        DiceConvention convention,
        string label,
        string formula,
        ResolutionRule? rule = null)
    {
        ArgumentNullException.ThrowIfNull(convention);

        var formattedFormula = FormatForConvention(convention, formula);
        var conventionType = convention.Type.ToString();

        string? resolutionDescription = null;
        if (rule is not null)
        {
            resolutionDescription = GetResolutionRuleDescription(rule);
        }

        return new AiRollRequest
        {
            Label = label,
            Formula = formattedFormula,
            ConventionType = conventionType,
            ResolutionDescription = resolutionDescription
        };
    }

    /// <inheritdoc />
    public AiContextValidationResult ValidateProposedActions(
        IReadOnlyList<AiProposedAction> actions,
        GameSystemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();

        // Build valid condition names set
        var validConditions = definition.ConditionSet
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build valid action slot names set
        var validActionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.ActionEconomy?.TurnStructure?.Slots is { Count: > 0 } slots)
        {
            foreach (var slot in slots)
            {
                validActionTypes.Add(slot.Name);
            }
        }

        foreach (var action in actions)
        {
            // Validate condition names
            if (!string.IsNullOrEmpty(action.ConditionName) && validConditions.Count > 0)
            {
                if (!validConditions.Contains(action.ConditionName))
                {
                    errors.Add($"Unknown condition '{action.ConditionName}' for this game system. " +
                               $"Valid conditions: {string.Join(", ", validConditions)}");
                }
            }

            // Validate action types against action economy (if defined)
            if (!string.IsNullOrEmpty(action.ActionType) && validActionTypes.Count > 0)
            {
                if (!validActionTypes.Contains(action.ActionType))
                {
                    errors.Add($"Unknown action type '{action.ActionType}' for this game system. " +
                               $"Valid action types: {string.Join(", ", validActionTypes)}");
                }
            }
        }

        return new AiContextValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string FormatForConvention(DiceConvention convention, string formula)
    {
        // Format the formula according to the convention type
        return convention.Type switch
        {
            DiceConventionType.DicePoolSuccess => FormatPoolNotation(convention, formula),
            DiceConventionType.Fudge => FormatFudgeNotation(formula),
            DiceConventionType.StepDice => FormatStepDiceNotation(convention, formula),
            DiceConventionType.Percentile => FormatPercentileNotation(formula),
            DiceConventionType.FixedDiceThreshold => FormatThresholdNotation(formula),
            _ => formula // SingleDieModifier and Expression use the formula as-is
        };
    }

    private static string FormatPoolNotation(DiceConvention convention, string formula)
    {
        // For dice pool systems, ensure the notation includes the success threshold
        if (convention.SuccessThreshold.HasValue && !formula.Contains(">=") && !formula.Contains(">"))
        {
            return $"{formula}>={convention.SuccessThreshold.Value}";
        }
        return formula;
    }

    private static string FormatFudgeNotation(string formula)
    {
        // Ensure Fudge notation uses dF
        if (!formula.Contains("dF", StringComparison.OrdinalIgnoreCase))
        {
            // Try to convert standard notation to Fudge
            if (int.TryParse(formula.Replace("d", "").Split('+')[0].Split('-')[0], out var count))
            {
                return $"{count}dF";
            }
        }
        return formula;
    }

    private static string FormatStepDiceNotation(DiceConvention convention, string formula)
    {
        // Step dice use the die from the convention's ladder
        if (convention.StepDiceLadder is { Count: > 0 } && !string.IsNullOrEmpty(convention.Die))
        {
            return $"1{convention.Die}";
        }
        return formula;
    }

    private static string FormatPercentileNotation(string formula)
    {
        // Percentile systems use d100
        if (!formula.Contains("d100", StringComparison.OrdinalIgnoreCase) &&
            !formula.Contains("d%", StringComparison.OrdinalIgnoreCase))
        {
            return "1d100";
        }
        return formula;
    }

    private static string FormatThresholdNotation(string formula)
    {
        // PbtA-style: typically 2d6+stat
        return formula;
    }

    private static void AppendAiGuidance(StringBuilder sb, AiGuidance guidance)
    {
        sb.AppendLine("### AI Guidance");

        if (!string.IsNullOrWhiteSpace(guidance.SystemPromptNotes))
        {
            sb.AppendLine(guidance.SystemPromptNotes);
        }

        if (!string.IsNullOrWhiteSpace(guidance.ToneGuidance))
        {
            sb.AppendLine($"**Tone:** {guidance.ToneGuidance}");
        }

        if (!string.IsNullOrWhiteSpace(guidance.MechanicalNotes))
        {
            sb.AppendLine($"**Mechanics:** {guidance.MechanicalNotes}");
        }

        if (guidance.CommonMistakes.Count > 0)
        {
            sb.AppendLine("**Common Mistakes to Avoid:**");
            foreach (var mistake in guidance.CommonMistakes)
            {
                sb.AppendLine($"- {mistake}");
            }
        }

        if (!string.IsNullOrWhiteSpace(guidance.RollFormatExample))
        {
            sb.AppendLine($"**Roll Format Example:** {guidance.RollFormatExample}");
        }

        sb.AppendLine();
    }

    private static string GetConventionTypeDescription(DiceConventionType type)
    {
        return type switch
        {
            DiceConventionType.SingleDieModifier => "Single Die + Modifier",
            DiceConventionType.DicePoolSuccess => "Dice Pool (count successes)",
            DiceConventionType.FixedDiceThreshold => "Fixed Dice + Threshold Bands",
            DiceConventionType.Fudge => "Fudge/FATE Dice",
            DiceConventionType.StepDice => "Step Dice",
            DiceConventionType.Percentile => "Percentile (d100)",
            DiceConventionType.Expression => "Custom Expression",
            _ => type.ToString()
        };
    }

    private static string GetDefaultConventionDescription(DiceConvention convention)
    {
        if (!string.IsNullOrEmpty(convention.Die))
        {
            return $"Roll {convention.Die}";
        }
        return "Custom dice mechanic";
    }

    private static string GetResolutionRuleDescription(ResolutionRule rule)
    {
        return rule.Type switch
        {
            ResolutionRuleType.TargetNumber =>
                $"Roll {rule.Comparison ?? ">="} target ({rule.TargetSource ?? "DC"})" +
                (rule.CriticalSuccess is not null ? $", crit on natural {rule.CriticalSuccess.NaturalRoll}" : "") +
                (rule.CriticalFailure is not null ? $", fumble on natural {rule.CriticalFailure.NaturalRoll}" : ""),
            ResolutionRuleType.Opposed =>
                $"Opposed roll (tie-breaker: {rule.TieBreaker ?? "reroll"})",
            ResolutionRuleType.DegreesOfSuccess =>
                $"Degrees of success: {string.Join(", ", rule.DegreesOfSuccess?.Select(d => d.Name) ?? [])}",
            ResolutionRuleType.Margin =>
                $"Margin of success vs {rule.TargetSource ?? "target"}",
            ResolutionRuleType.ThresholdBands =>
                $"Threshold bands: {string.Join(", ", rule.DegreesOfSuccess?.Select(d => $"{d.Name} ({d.MinValue}-{d.MaxValue})") ?? [])}",
            _ => rule.Type.ToString()
        };
    }

    private static string GetActionEconomyDescription(ActionEconomyDefinition economy)
    {
        return economy.Type switch
        {
            ActionEconomyType.NamedSlots => "Named action slots per turn",
            ActionEconomyType.ActionPoints => $"Action point pool ({economy.PointsPerTurn} points/turn)",
            ActionEconomyType.MultiActionPenalty => $"Multi-action with penalty (max {economy.MaxActions} actions, {economy.PenaltyIncrement} penalty/action)",
            ActionEconomyType.Freeform => "Freeform (narrative turns)",
            _ => economy.Type.ToString()
        };
    }

    private static bool IsDefinitionIncomplete(GameSystemDefinition definition)
    {
        return definition.DiceConventions.Count == 0 ||
               definition.ResolutionRules.Count == 0;
    }
}
