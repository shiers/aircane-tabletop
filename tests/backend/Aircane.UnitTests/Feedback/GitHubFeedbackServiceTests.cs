using System.Net;
using System.Text.Json;
using Aircane.Application.Configuration;
using Aircane.Application.Feedback;
using Aircane.Infrastructure.Feedback;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Feedback;

/// <summary>
/// Unit tests for <see cref="GitHubFeedbackService"/>. HTTP is stubbed with a capturing
/// <see cref="HttpMessageHandler"/> (same pattern as the AI provider tests); no real network
/// calls are made and no real token is used.
/// </summary>
public class GitHubFeedbackServiceTests
{
    private static FeedbackSettings ConfiguredSettings() => new()
    {
        GitHubToken = "github_pat_test_placeholder",
        GitHubOwner = "owner",
        GitHubRepo = "aircane-tabletop",
    };

    private static FeedbackDto FullDto() => new()
    {
        Summary = "AI DM stopped responding after rolling initiative",
        Description = "I rolled initiative and the AI never continued narrating.",
        StepsToReproduce = "1. Start a session\n2. Begin combat\n3. Roll initiative",
        ExpectedBehaviour = "AI should narrate the start of combat",
        DiagnosticContext = new FeedbackDiagnosticContext
        {
            Platform = "Windows 11",
            AppVersion = "0.1.0",
            IsTauri = true,
            TauriVersion = "2.0.0",
            ActiveRoute = "/sessions/abc123/host",
            SessionId = "abc123",
            CampaignId = "def456",
            UserRole = "Host",
            AiProvider = "Ollama",
            AiModel = "llama3.2",
            EmbeddingProvider = "Ollama",
            EmbeddingModel = "nomic-embed-text",
        },
        RecentEvents =
        [
            "2026-09-29T12:34:45Z [AI] AINarrationCompleted",
            "2026-09-29T12:34:50Z [Player] RollRecorded — 1d20+5 = 17",
        ],
        ConsoleErrors =
        [
            "TypeError: Cannot read properties of undefined (reading 'hp') at CombatTracker.vue:142",
        ],
    };

    private static GitHubFeedbackService BuildService(
        FeedbackCapturingHandler handler,
        FeedbackSettings settings)
    {
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        return new GitHubFeedbackService(factory, settings, NullLogger<GitHubFeedbackService>.Instance);
    }

    private static FeedbackCapturingHandler SuccessHandler()
    {
        var body = JsonSerializer.Serialize(new
        {
            html_url = "https://github.com/owner/aircane-tabletop/issues/42",
            number = 42,
        });
        return new FeedbackCapturingHandler(HttpStatusCode.Created, body);
    }

    [Fact]
    public async Task GitHubFeedbackService_FormatsIssueBody_IncludesAllDiagnosticFields()
    {
        var handler = SuccessHandler();
        var service = BuildService(handler, ConfiguredSettings());

        var result = await service.SubmitAsync(FullDto(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(42, result.IssueNumber);
        Assert.Equal("https://github.com/owner/aircane-tabletop/issues/42", result.IssueUrl);

        // The outgoing payload JSON contains title/body/labels/assignees.
        var payload = JsonDocument.Parse(handler.LastRequestBody!).RootElement;

        Assert.Equal(
            "[Beta] AI DM stopped responding after rolling initiative",
            payload.GetProperty("title").GetString());

        var labels = payload.GetProperty("labels").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("bug", labels);
        Assert.Contains("beta-feedback", labels);

        // Assignee defaults to the configured owner.
        var assignees = payload.GetProperty("assignees").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("owner", assignees);

        var body = payload.GetProperty("body").GetString()!;

        // Section headers and both fenced code blocks.
        Assert.Contains("## Description", body);
        Assert.Contains("## Steps to reproduce", body);
        Assert.Contains("## Expected behaviour", body);
        Assert.Contains("## Diagnostic context", body);
        Assert.Contains("## Recent session events (last 10)", body);
        Assert.Contains("## Console errors (last 5)", body);
        Assert.Contains("*Submitted via Aircane Tabletop in-app feedback. Beta build.*", body);

        // Every diagnostic field value appears in the body.
        Assert.Contains("Windows 11", body);
        Assert.Contains("0.1.0", body);
        Assert.Contains("Ollama (llama3.2)", body);
        Assert.Contains("Ollama (nomic-embed-text)", body);
        Assert.Contains("/sessions/abc123/host", body);
        Assert.Contains("abc123", body);
        Assert.Contains("def456", body);
        Assert.Contains("Host", body);

        // Both event/error lines appear inside the code sections.
        Assert.Contains("AINarrationCompleted", body);
        Assert.Contains("RollRecorded", body);
        Assert.Contains("CombatTracker.vue:142", body);

        // Request target + headers.
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://api.github.com/repos/owner/aircane-tabletop/issues",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task GitHubFeedbackService_SanitizesHtml_InSummaryAndDescription()
    {
        var handler = SuccessHandler();
        var service = BuildService(handler, ConfiguredSettings());

        var dto = new FeedbackDto
        {
            Summary = "<script>alert('xss')</script>Broken dice",
            Description = "<b>bold</b> and <img src=x onerror=1> text",
        };

        await service.SubmitAsync(dto, CancellationToken.None);

        var payload = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var title = payload.GetProperty("title").GetString()!;
        var body = payload.GetProperty("body").GetString()!;

        Assert.DoesNotContain("<script>", body);
        Assert.DoesNotContain("<b>", body);
        Assert.DoesNotContain("<img", body);
        Assert.DoesNotContain("<script>", title);

        // Visible text survives.
        Assert.Contains("Broken dice", title);
        Assert.Contains("bold", body);
    }

    [Fact]
    public async Task GitHubFeedbackService_LongInput_TruncatedTo2000Chars()
    {
        var handler = SuccessHandler();
        var service = BuildService(handler, ConfiguredSettings());

        var dto = new FeedbackDto
        {
            Summary = new string('a', 5000),
            Description = new string('b', 5000),
        };

        await service.SubmitAsync(dto, CancellationToken.None);

        var payload = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var title = payload.GetProperty("title").GetString()!;
        var body = payload.GetProperty("body").GetString()!;

        // Title is "[Beta] " + up to 2000 chars of summary.
        Assert.True(title.Length <= "[Beta] ".Length + 2000);
        // The sanitized description is capped at 2000 chars: the longest run of 'b' in the body
        // (the description block) must not exceed the cap.
        var longestBRun = body.Split('\n').Max(line => line.Count(c => c == 'b'));
        Assert.True(longestBRun <= 2000, $"Expected a 'b' run <= 2000, found {longestBRun}.");
    }

    [Fact]
    public async Task GitHubFeedbackService_NoToken_ThrowsNotConfigured()
    {
        var handler = SuccessHandler();
        var service = BuildService(handler, new FeedbackSettings
        {
            GitHubToken = "",
            GitHubOwner = "owner",
            GitHubRepo = "aircane-tabletop",
        });

        await Assert.ThrowsAsync<FeedbackNotConfiguredException>(() =>
            service.SubmitAsync(FullDto(), CancellationToken.None));

        // No HTTP call was made.
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task GitHubFeedbackService_NullDiagnosticFields_DoesNotCrash()
    {
        var handler = SuccessHandler();
        var service = BuildService(handler, ConfiguredSettings());

        var dto = new FeedbackDto
        {
            Summary = "Something broke",
            Description = "Not sure what happened",
            DiagnosticContext = null,
            RecentEvents = null,
            ConsoleErrors = null,
        };

        var result = await service.SubmitAsync(dto, CancellationToken.None);

        Assert.True(result.Success);
        var payload = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var body = payload.GetProperty("body").GetString()!;
        Assert.DoesNotContain("null", body);
    }

    // ── Test infrastructure ─────────────────────────────────────────────────────

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class FeedbackCapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FeedbackCapturingHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
