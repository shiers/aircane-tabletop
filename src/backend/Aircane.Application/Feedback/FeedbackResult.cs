namespace Aircane.Application.Feedback;

/// <summary>
/// The outcome of submitting a bug report. Carries only non-sensitive data returned to the
/// client — never the GitHub token.
/// </summary>
public sealed class FeedbackResult
{
    /// <summary>Whether the GitHub issue was created successfully.</summary>
    public bool Success { get; init; }

    /// <summary>The HTML URL of the created GitHub issue, when successful.</summary>
    public string? IssueUrl { get; init; }

    /// <summary>The number of the created GitHub issue, when successful.</summary>
    public int? IssueNumber { get; init; }
}
