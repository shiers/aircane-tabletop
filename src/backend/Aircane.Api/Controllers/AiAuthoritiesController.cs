using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Provides AI authority configuration information.
/// Used by the frontend to display authority level options and their action rules.
/// </summary>
[ApiController]
[Route("api/ai/authorities")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class AiAuthoritiesController : ControllerBase
{
    private readonly IAiAuthorityService _authorityService;

    public AiAuthoritiesController(IAiAuthorityService authorityService)
    {
        _authorityService = authorityService;
    }

    /// <summary>
    /// Returns all available AI authority configurations with their action rules.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AiAuthorityConfigurationDto>), StatusCodes.Status200OK)]
    public IActionResult GetAllAuthorities()
    {
        var configs = _authorityService.GetAllConfigurations();
        var dtos = configs.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Returns the configuration for a specific AI authority level.
    /// </summary>
    [HttpGet("{authority}")]
    [ProducesResponseType(typeof(AiAuthorityConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetAuthority(string authority)
    {
        if (!Enum.TryParse<AiAuthority>(authority, ignoreCase: true, out var aiAuthority))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid AI authority",
                Detail = $"'{authority}' is not a valid AI authority level. Valid values: {string.Join(", ", Enum.GetNames<AiAuthority>())}",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var config = _authorityService.GetConfiguration(aiAuthority);
        return Ok(MapToDto(config));
    }

    /// <summary>
    /// Checks whether a specific action type requires approval at the given authority level.
    /// </summary>
    [HttpGet("{authority}/actions/{actionType}/requires-approval")]
    [ProducesResponseType(typeof(ActionApprovalResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult CheckActionApproval(string authority, string actionType)
    {
        if (!Enum.TryParse<AiAuthority>(authority, ignoreCase: true, out var aiAuthority))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid AI authority",
                Detail = $"'{authority}' is not a valid AI authority level.",
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

        var requiresApproval = _authorityService.RequiresApproval(aiAuthority, action);
        var canAutoApply = _authorityService.CanAutoApply(aiAuthority, action);

        return Ok(new ActionApprovalResult(aiAuthority, action, requiresApproval, canAutoApply));
    }

    private static AiAuthorityConfigurationDto MapToDto(AiAuthorityConfiguration config)
    {
        return new AiAuthorityConfigurationDto(
            config.Authority,
            config.DisplayName,
            config.Description,
            config.AutoApplyActions.Select(a => a.ToString()).OrderBy(s => s).ToList(),
            config.RequiresApprovalActions.Select(a => a.ToString()).OrderBy(s => s).ToList()
        );
    }
}

/// <summary>
/// DTO for AI authority configuration responses.
/// </summary>
public sealed record AiAuthorityConfigurationDto(
    AiAuthority Authority,
    string DisplayName,
    string Description,
    IReadOnlyList<string> AutoApplyActions,
    IReadOnlyList<string> RequiresApprovalActions);

/// <summary>
/// Result of an action approval check.
/// </summary>
public sealed record ActionApprovalResult(
    AiAuthority Authority,
    AiActionType ActionType,
    bool RequiresApproval,
    bool CanAutoApply);
