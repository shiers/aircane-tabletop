using Aircane.Domain.Enums;

namespace Aircane.Application.AiRuntime;

/// <summary>
/// Describes the full configuration for an AI role, including its display metadata
/// and the set of capabilities it grants.
/// </summary>
public sealed record AiRoleConfiguration
{
    /// <summary>The AI role this configuration describes.</summary>
    public AiRole Role { get; init; }

    /// <summary>Human-readable display name for the role.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Short description of what this role does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The set of capabilities granted by this role.</summary>
    public IReadOnlySet<AiRoleCapability> Capabilities { get; init; } = new HashSet<AiRoleCapability>();
}
