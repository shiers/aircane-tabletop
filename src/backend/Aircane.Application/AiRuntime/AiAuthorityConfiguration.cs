using Aircane.Application.Abstractions;
using Aircane.Domain.Enums;

namespace Aircane.Application.AiRuntime;

/// <summary>
/// Describes the full configuration for an AI authority level, including its display metadata
/// and the rules governing which actions require approval vs. auto-apply.
/// </summary>
public sealed record AiAuthorityConfiguration
{
    /// <summary>The AI authority level this configuration describes.</summary>
    public AiAuthority Authority { get; init; }

    /// <summary>Human-readable display name for the authority level.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Short description of what this authority level allows.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The set of action types that can be auto-applied without host approval at this authority level.
    /// </summary>
    public IReadOnlySet<AiActionType> AutoApplyActions { get; init; } = new HashSet<AiActionType>();

    /// <summary>
    /// The set of action types that require host approval at this authority level.
    /// </summary>
    public IReadOnlySet<AiActionType> RequiresApprovalActions { get; init; } = new HashSet<AiActionType>();
}
