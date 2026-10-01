using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Ai;
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

    public RulesQuestionController(IRulesQuestionService rulesQuestionService)
    {
        _rulesQuestionService = rulesQuestionService;
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
            var response = await _rulesQuestionService.AskAsync(request, cancellationToken);
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
}
