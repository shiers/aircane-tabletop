using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.GameSystems;
using Aircane.Application.GameSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Campaign CRUD — create, list, retrieve, update, and delete campaigns.
/// </summary>
[ApiController]
[Route("api/campaigns")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.DmOrHost)]
public sealed class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaigns;
    private readonly ISystemRegistry _registry;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(
        ICampaignService campaigns,
        ISystemRegistry registry,
        ILogger<CampaignsController> logger)
    {
        _campaigns = campaigns;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new campaign.
    /// Accepts either a gameSystemDefinitionId (preferred) or legacy GameSystem/Ruleset strings.
    /// When gameSystemDefinitionId is provided, GameSystem and Ruleset are populated from definition metadata.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCampaign(
        [FromBody] CreateCampaignRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Campaign name is required.",
                Status = StatusCodes.Status400BadRequest,
            });

        // If a game system definition ID is provided, resolve GameSystem/Ruleset from it
        var effectiveRequest = request;
        if (request.GameSystemDefinitionId.HasValue)
        {
            try
            {
                var definition = await _registry.GetByIdAsync(request.GameSystemDefinitionId.Value, cancellationToken);
                effectiveRequest = request with
                {
                    GameSystem = definition.Name,
                    Ruleset = definition.Version,
                };
            }
            catch (KeyNotFoundException)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation failed",
                    Detail = "The specified game system definition was not found.",
                    Status = StatusCodes.Status400BadRequest,
                });
            }
        }
        else
        {
            // Legacy path: require GameSystem and Ruleset strings
            if (string.IsNullOrWhiteSpace(request.GameSystem))
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation failed",
                    Detail = "Game system is required (provide gameSystemDefinitionId or gameSystem string).",
                    Status = StatusCodes.Status400BadRequest,
                });

            if (string.IsNullOrWhiteSpace(request.Ruleset))
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation failed",
                    Detail = "Ruleset is required (provide gameSystemDefinitionId or ruleset string).",
                    Status = StatusCodes.Status400BadRequest,
                });
        }

        var dto = await _campaigns.CreateCampaignAsync(effectiveRequest, cancellationToken);
        return CreatedAtAction(nameof(GetCampaign), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Returns all campaigns ordered by creation date descending.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCampaigns(CancellationToken cancellationToken)
    {
        var campaigns = await _campaigns.ListCampaignsAsync(cancellationToken);
        return Ok(campaigns);
    }

    /// <summary>
    /// Returns a single campaign by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCampaign(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _campaigns.GetCampaignAsync(id, cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Updates campaign settings. Only provided (non-null) fields are applied.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCampaign(
        Guid id,
        [FromBody] UpdateCampaignRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _campaigns.UpdateCampaignAsync(id, request, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Deletes a campaign by ID.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCampaign(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _campaigns.DeleteCampaignAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Migrates a campaign to a different Game System Definition version.
    /// Warns about potential incompatibilities with existing characters and state.
    /// </summary>
    [HttpPut("{id:guid}/migrate-system")]
    [ProducesResponseType(typeof(MigrateSystemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MigrateSystem(
        Guid id,
        [FromBody] MigrateSystemRequest request,
        CancellationToken cancellationToken)
    {
        // Verify campaign exists
        var campaign = await _campaigns.GetCampaignAsync(id, cancellationToken);
        if (campaign is null)
            return NotFound();

        // Verify the target definition exists
        Domain.Entities.GameSystems.GameSystemDefinition targetDefinition;
        try
        {
            targetDefinition = await _registry.GetByIdAsync(request.GameSystemDefinitionId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "The target game system definition was not found.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (!targetDefinition.IsActive)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "Cannot migrate to an inactive game system definition.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        // Update the campaign's game system binding
        var warnings = new List<string>();
        if (campaign.GameSystem != targetDefinition.Name)
        {
            warnings.Add($"Game system changing from '{campaign.GameSystem}' to '{targetDefinition.Name}'. Existing characters may need field remapping.");
        }

        var updateRequest = new UpdateCampaignRequest(
            GameSystem: targetDefinition.Name,
            Ruleset: targetDefinition.Version,
            GameSystemDefinitionId: request.GameSystemDefinitionId);

        try
        {
            await _campaigns.UpdateCampaignAsync(id, updateRequest, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        return Ok(new MigrateSystemResponse(
            CampaignId: id,
            NewGameSystemDefinitionId: targetDefinition.Id,
            NewGameSystemName: targetDefinition.Name,
            NewVersion: targetDefinition.Version,
            Warnings: warnings));
    }
}
