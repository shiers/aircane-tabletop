using Aircane.Application.Configuration;
using Aircane.Application.Feedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aircane.Api.Controllers;

/// <summary>
/// Receives in-app bug reports and proxies them to the configured GitHub issue tracker. The
/// endpoint is anonymous — a tester may hit a bug before holding a session token (e.g. on the
/// join screen) — and is always rate limited (see the "feedback" policy) because it is
/// unauthenticated and creates real GitHub issues.
/// <para>
/// SECURITY: the GitHub token is never returned to the client or logged; the browser never calls
/// GitHub directly — this endpoint is the only submission surface.
/// </para>
/// </summary>
[ApiController]
[Route("api/feedback")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingSettings.FeedbackPolicy)]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedbackService;

    public FeedbackController(IFeedbackService feedbackService)
    {
        _feedbackService = feedbackService;
    }

    /// <summary>
    /// Submits a bug report, creating a GitHub issue with the supplied free text and
    /// auto-captured diagnostic context.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> Submit(
        [FromBody] FeedbackDto dto,
        CancellationToken cancellationToken)
    {
        if (dto is null)
            return BadRequest(new { error = "Request body is required." });

        if (string.IsNullOrWhiteSpace(dto.Summary))
            return BadRequest(new { error = "Summary is required." });

        if (string.IsNullOrWhiteSpace(dto.Description))
            return BadRequest(new { error = "Description is required." });

        try
        {
            var result = await _feedbackService.SubmitAsync(dto, cancellationToken);
            return Ok(new
            {
                success = result.Success,
                issueUrl = result.IssueUrl,
                issueNumber = result.IssueNumber,
            });
        }
        catch (FeedbackNotConfiguredException)
        {
            return StatusCode(503, new { error = "Feedback is not configured on this instance." });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"GitHub request failed: {ex.Message}" });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
