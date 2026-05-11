using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Provides AI authority configuration and action approval queries.
/// Used by the AI runtime to determine whether an action requires host approval
/// or can be auto-applied at the current authority level.
/// </summary>
public interface IAiAuthorityService
{
    /// <summary>
    /// Gets the full configuration for a specific AI authority level.
    /// </summary>
    AiAuthorityConfiguration GetConfiguration(AiAuthority authority);

    /// <summary>
    /// Gets configurations for all available AI authority levels.
    /// </summary>
    IReadOnlyList<AiAuthorityConfiguration> GetAllConfigurations();

    /// <summary>
    /// Determines whether the given action type requires host approval at the specified authority level.
    /// </summary>
    bool RequiresApproval(AiAuthority authority, AiActionType actionType);

    /// <summary>
    /// Determines whether the given action type can be auto-applied (without host approval)
    /// at the specified authority level.
    /// </summary>
    bool CanAutoApply(AiAuthority authority, AiActionType actionType);
}
