namespace Aircane.Domain.Enums;

/// <summary>
/// Defines how a game system resolves action outcomes.
/// </summary>
public enum ResolutionRuleType
{
    /// <summary>Roll compared against a target number (roll >= DC).</summary>
    TargetNumber,

    /// <summary>Attacker roll vs defender roll with tie-breaking.</summary>
    Opposed,

    /// <summary>Multiple thresholds for critical/success/failure/fumble (PF2e).</summary>
    DegreesOfSuccess,

    /// <summary>Success margin calculation (roll - target).</summary>
    Margin,

    /// <summary>Fixed outcome tiers based on total (PbtA: 6-/7-9/10+).</summary>
    ThresholdBands
}
