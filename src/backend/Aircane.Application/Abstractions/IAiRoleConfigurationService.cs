using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Provides AI role configuration and capability queries.
/// Used by the AI runtime to determine what actions are permitted for a given role.
/// </summary>
public interface IAiRoleConfigurationService
{
    /// <summary>
    /// Gets the full configuration for a specific AI role.
    /// </summary>
    AiRoleConfiguration GetConfiguration(AiRole role);

    /// <summary>
    /// Gets configurations for all available AI roles.
    /// </summary>
    IReadOnlyList<AiRoleConfiguration> GetAllConfigurations();

    /// <summary>
    /// Checks whether a specific capability is granted by the given role.
    /// </summary>
    bool HasCapability(AiRole role, AiRoleCapability capability);

    /// <summary>
    /// Gets all capabilities granted by the given role.
    /// </summary>
    IReadOnlySet<AiRoleCapability> GetCapabilities(AiRole role);

    /// <summary>
    /// Determines whether the given AI action type is permitted for the specified role.
    /// Maps AI action types to required capabilities.
    /// </summary>
    bool IsActionPermitted(AiRole role, AiActionType actionType);
}
