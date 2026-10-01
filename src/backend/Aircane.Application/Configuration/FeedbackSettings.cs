namespace Aircane.Application.Configuration;

/// <summary>
/// Configuration for the in-app "Report a bug" feedback feature, which proxies tester
/// bug reports to the Aircane GitHub repository's issue tracker via the GitHub REST API.
/// <para>
/// Bound from the <c>Feedback</c> section of configuration. See <see cref="SectionName"/>.
/// </para>
/// <para>
/// SECURITY: <see cref="GitHubToken"/> is a secret. It is supplied via environment variable
/// (<c>Feedback__GitHubToken</c>) or .NET user secrets, never committed to the repo. It must
/// never be returned to any client, logged, or stored in the database.
/// </para>
/// </summary>
public sealed class FeedbackSettings
{
    public const string SectionName = "Feedback";

    /// <summary>
    /// Fine-grained GitHub personal access token with <b>Issues: Read and Write</b> scoped to
    /// the Aircane repository only. SECRET — never return to a client or log. When empty, the
    /// feedback endpoint reports "not configured" (HTTP 503) instead of attempting a submission.
    /// </summary>
    public string? GitHubToken { get; set; }

    /// <summary>Owner (user or org) of the GitHub repository that receives bug reports.</summary>
    public string? GitHubOwner { get; set; }

    /// <summary>Name of the GitHub repository that receives bug reports.</summary>
    public string? GitHubRepo { get; set; }

    /// <summary>
    /// Optional GitHub login to assign created issues to. When empty, <see cref="GitHubOwner"/>
    /// is used as the assignee; when both are empty, no assignee is set.
    /// </summary>
    public string? Assignee { get; set; }
}
