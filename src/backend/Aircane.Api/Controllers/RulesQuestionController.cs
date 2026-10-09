using System.Security.Claims;
using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Handles rules question requests using RAG-grounded retrieval and AI generation.
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public class RulesQuestionController : ControllerBase
{
    private readonly IRulesQuestionService _rulesQuestionService;
    private readonly IRetrievalService _retrievalService;

    public RulesQuestionController(
        IRulesQuestionService rulesQuestionService,
        IRetrievalService retrievalService)
    {
        _rulesQuestionService = rulesQuestionService;
        _retrievalService = retrievalService;
    }

    /// <summary>
    /// Lists the ruleset values present on imported documents, optionally for one game system,
    /// to populate the Rules Lookup ruleset dropdown.
    /// </summary>
    [HttpGet("rules-question/rulesets")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRulesets(
        [FromQuery] string? gameSystem,
        CancellationToken cancellationToken)
    {
        var rulesets = await _retrievalService.GetAvailableRulesetsAsync(gameSystem, cancellationToken);
        return Ok(rulesets);
    }

    /// <summary>
    /// Asks a rules question and returns an AI-generated answer grounded in indexed source documents.
    /// </summary>
    /// <param name="request">The rules question with optional game system and ruleset context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer with citations and source support indicator.</returns>
    [HttpPost("rules-question")]
    public async Task<ActionResult<RulesQuestionResponse>> AskRulesQuestion(
        [FromBody] RulesQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question is required." });

        try
        {
            var response = await _rulesQuestionService.AskAsync(request, cancellationToken, ResolveRequesterRole());
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            // AI provider configuration or response errors (missing key, bad response, rate limit, etc.)
            return StatusCode(503, new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"AI provider request failed: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"An unexpected error occurred: {ex.Message}" });
        }
    }

    /// <summary>
    /// Resolves the caller's participant role from the role claim issued by ParticipantTokenService.
    /// An unauthenticated caller can only reach this endpoint when the authorization policies are
    /// opened up (Development or Aircane:TrustLocalHost, i.e. the local desktop user), who is the
    /// host. An authenticated caller with a missing or unrecognised role gets the least-privileged
    /// role.
    /// </summary>
    private ParticipantRole ResolveRequesterRole()
    {
        if (User.Identity?.IsAuthenticated != true)
            return ParticipantRole.Host;

        var claim = User.FindFirst(ClaimTypes.Role)?.Value;
        return Enum.TryParse<ParticipantRole>(claim, ignoreCase: true, out var role)
            && Enum.IsDefined(role)
            ? role
            : ParticipantRole.Player;
    }
}
