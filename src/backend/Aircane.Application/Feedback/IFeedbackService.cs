namespace Aircane.Application.Feedback;

/// <summary>
/// Submits a tester bug report to the configured issue tracker. Implementations proxy the
/// submission server-side so the GitHub token is never exposed to the browser.
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Creates an issue from the supplied report.
    /// </summary>
    /// <param name="dto">The bug report. <c>Summary</c> and <c>Description</c> are required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created issue URL and number.</returns>
    /// <exception cref="FeedbackNotConfiguredException">
    /// Thrown when the feedback feature is not configured on this instance (no GitHub token).
    /// </exception>
    Task<FeedbackResult> SubmitAsync(FeedbackDto dto, CancellationToken ct);
}
