namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// The result of resolving a roll against a resolution rule.
/// Contains exactly one outcome classification.
/// </summary>
public record ResolutionOutcome
{
    /// <summary>The outcome classification (e.g., "Success", "Failure", "Critical Success", "Attacker Wins").</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>The margin of success/failure for margin-based rules. Null for non-margin rules.</summary>
    public int? Margin { get; init; }

    /// <summary>Whether the outcome is considered a success.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Whether the outcome is a critical (natural 20/1 or equivalent).</summary>
    public bool IsCritical { get; init; }

    /// <summary>Non-null when resolution fails (e.g., missing attribute). Contains the error description.</summary>
    public string? Error { get; init; }
}
