using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// The encounter as the table may see it, for players. The full encounter
/// (<see cref="CombatController"/>) stays with the host and a human DM: it has every NPC's hit points.
/// A separate controller because the full one is DM-only at class level.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId:guid}/combat/public")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.SessionMember)]
public sealed class CombatViewController : ControllerBase
{
    private readonly ICampaignStateService _stateService;
    private readonly ISessionHostingService _sessions;

    public CombatViewController(ICampaignStateService stateService, ISessionHostingService sessions)
    {
        _stateService = stateService;
        _sessions = sessions;
    }

    /// <summary>
    /// The live encounter of the session's campaign, player-safe (<see cref="PublicEncounterDto"/>), or 204
    /// when there is none. Any approved participant of the session.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PublicEncounterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicEncounter(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
            return NotFound();

        var state = await _stateService.LoadStateAsync(session.CampaignId, cancellationToken);
        var encounter = CombatController.ReadEncounter(state.StateJson);
        if (encounter is null)
            return NoContent();

        // Projected here, on the server: the hidden numbers never leave it.
        Response.Headers.CacheControl = "no-store";
        return Ok(PublicEncounterDto.From(CombatController.ToDto(encounter)));
    }
}
