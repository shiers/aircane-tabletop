namespace Aircane.Application.Feedback;

/// <summary>
/// A bug report submitted via the in-app "Report a bug" feature. The tester supplies the
/// free-text fields; the app auto-captures the diagnostic context, recent session events, and
/// console errors.
/// <para>
/// This DTO carries no secrets: the GitHub token lives only in server configuration and is
/// never part of the request or response.
/// </para>
/// </summary>
public sealed class FeedbackDto
{
    /// <summary>One-line summary of the bug (required).</summary>
    public string? Summary { get; set; }

    /// <summary>Free-text description of what happened (required).</summary>
    public string? Description { get; set; }

    /// <summary>Optional free-text steps to reproduce.</summary>
    public string? StepsToReproduce { get; set; }

    /// <summary>Optional free-text expected behaviour.</summary>
    public string? ExpectedBehaviour { get; set; }

    /// <summary>Auto-captured diagnostic context. All fields optional/nullable.</summary>
    public FeedbackDiagnosticContext? DiagnosticContext { get; set; }

    /// <summary>Short, non-sensitive summaries of the last session events (type + timestamp only).</summary>
    public IReadOnlyList<string>? RecentEvents { get; set; }

    /// <summary>Trimmed console error strings captured in the browser.</summary>
    public IReadOnlyList<string>? ConsoleErrors { get; set; }
}

/// <summary>
/// Auto-captured, non-sensitive diagnostic context. Contains no campaign content, character
/// data, narration text, PDF content, or API keys.
/// </summary>
public sealed class FeedbackDiagnosticContext
{
    public string? Platform { get; set; }
    public string? UserAgent { get; set; }
    public string? AppVersion { get; set; }
    public bool? IsTauri { get; set; }
    public string? TauriVersion { get; set; }
    public string? ActiveRoute { get; set; }
    public string? SessionId { get; set; }
    public string? CampaignId { get; set; }
    public string? UserRole { get; set; }
    public string? AiProvider { get; set; }
    public string? AiModel { get; set; }
    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingModel { get; set; }
}
