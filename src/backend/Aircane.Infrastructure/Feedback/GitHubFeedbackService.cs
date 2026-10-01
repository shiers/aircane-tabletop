using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aircane.Application.Configuration;
using Aircane.Application.Feedback;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Feedback;

/// <summary>
/// Submits bug reports to the configured GitHub repository's issue tracker via the GitHub REST
/// API. The GitHub token is read only from <see cref="FeedbackSettings"/> here; it is never
/// returned to a client, logged, or stored.
/// </summary>
public sealed class GitHubFeedbackService : IFeedbackService
{
    /// <summary>The name of the typed HttpClient used for GitHub requests.</summary>
    public const string HttpClientName = "GitHubFeedback";

    private static readonly string[] IssueLabels = ["bug", "beta-feedback"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FeedbackSettings _settings;
    private readonly ILogger<GitHubFeedbackService> _logger;

    public GitHubFeedbackService(
        IHttpClientFactory httpClientFactory,
        FeedbackSettings settings,
        ILogger<GitHubFeedbackService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<FeedbackResult> SubmitAsync(FeedbackDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.GitHubToken)
            || string.IsNullOrWhiteSpace(_settings.GitHubOwner)
            || string.IsNullOrWhiteSpace(_settings.GitHubRepo))
        {
            throw new FeedbackNotConfiguredException();
        }

        var summary = FeedbackSanitizer.Sanitize(dto.Summary);
        var description = FeedbackSanitizer.Sanitize(dto.Description);

        var title = GitHubIssueFormatter.BuildTitle(summary);
        var body = GitHubIssueFormatter.BuildBody(dto, description);

        var payload = new GitHubIssuePayload
        {
            Title = title,
            Body = body,
            Labels = IssueLabels,
            Assignees = ResolveAssignees(),
        };

        var requestUri = $"https://api.github.com/repos/{_settings.GitHubOwner}/{_settings.GitHubRepo}/issues";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.UserAgent.ParseAdd("Aircane-Tabletop");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.GitHubToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Never include the token (header) in the exception or log. Status + endpoint only.
            _logger.LogWarning(
                "GitHub issue creation failed with status {StatusCode} for {Owner}/{Repo}.",
                (int)response.StatusCode, _settings.GitHubOwner, _settings.GitHubRepo);

            throw new InvalidOperationException(
                $"GitHub rejected the feedback submission (status {(int)response.StatusCode}).");
        }

        var created = await response.Content.ReadFromJsonAsync<GitHubIssueResponse>(cancellationToken: ct);
        if (created is null)
        {
            throw new InvalidOperationException("GitHub returned an empty response for the created issue.");
        }

        return new FeedbackResult
        {
            Success = true,
            IssueUrl = created.HtmlUrl,
            IssueNumber = created.Number,
        };
    }

    private string[]? ResolveAssignees()
    {
        var assignee = !string.IsNullOrWhiteSpace(_settings.Assignee)
            ? _settings.Assignee
            : _settings.GitHubOwner;

        return string.IsNullOrWhiteSpace(assignee) ? null : [assignee];
    }

    private sealed class GitHubIssuePayload
    {
        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("body")]
        public required string Body { get; init; }

        [JsonPropertyName("labels")]
        public required string[] Labels { get; init; }

        [JsonPropertyName("assignees")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Assignees { get; init; }
    }

    private sealed class GitHubIssueResponse
    {
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("number")]
        public int Number { get; init; }
    }
}
