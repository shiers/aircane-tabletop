using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Provides AI role configuration information.
/// Used by the frontend to display role options and their capabilities.
/// </summary>
[ApiController]
[Route("api/ai/roles")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class AiRolesController : ControllerBase
{
    private readonly IAiRoleConfigurationService _roleConfig;

    public AiRolesController(IAiRoleConfigurationService roleConfig)
    {
        _roleConfig = roleConfig;
    }

    /// <summary>
    /// Returns all available AI role configurations with their capabilities.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AiRoleConfigurationDto>), StatusCodes.Status200OK)]
    public IActionResult GetAllRoles()
    {
        var configs = _roleConfig.GetAllConfigurations();
        var dtos = configs.Select(c => new AiRoleConfigurationDto(
            c.Role,
            c.DisplayName,
            c.Description,
            c.Capabilities.Select(cap => cap.ToString()).OrderBy(s => s).ToList()
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Returns the configuration for a specific AI role.
    /// </summary>
    [HttpGet("{role}")]
    [ProducesResponseType(typeof(AiRoleConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetRole(string role)
    {
        if (!Enum.TryParse<AiRole>(role, ignoreCase: true, out var aiRole))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid AI role",
                Detail = $"'{role}' is not a valid AI role. Valid values: {string.Join(", ", Enum.GetNames<AiRole>())}",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var config = _roleConfig.GetConfiguration(aiRole);
        var dto = new AiRoleConfigurationDto(
            config.Role,
            config.DisplayName,
            config.Description,
            config.Capabilities.Select(cap => cap.ToString()).OrderBy(s => s).ToList()
        );

        return Ok(dto);
    }

    /// <summary>
    /// Checks whether a specific action type is permitted for the given role.
    /// </summary>
    [HttpGet("{role}/actions/{actionType}/permitted")]
    [ProducesResponseType(typeof(ActionPermissionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult IsActionPermitted(string role, string actionType)
    {
        if (!Enum.TryParse<AiRole>(role, ignoreCase: true, out var aiRole))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid AI role",
                Detail = $"'{role}' is not a valid AI role.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (!Enum.TryParse<AiActionType>(actionType, ignoreCase: true, out var action))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid action type",
                Detail = $"'{actionType}' is not a valid AI action type.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var permitted = _roleConfig.IsActionPermitted(aiRole, action);
        return Ok(new ActionPermissionResult(aiRole, action, permitted));
    }
}

/// <summary>
/// DTO for AI role configuration responses.
/// </summary>
public sealed record AiRoleConfigurationDto(
    AiRole Role,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Capabilities);

/// <summary>
/// Result of an action permission check.
/// </summary>
public sealed record ActionPermissionResult(
    AiRole Role,
    AiActionType ActionType,
    bool IsPermitted);
