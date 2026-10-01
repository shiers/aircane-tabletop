using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the anonymous feedback endpoint. The default test factory supplies no
/// <c>Feedback:GitHubToken</c>, so a valid POST short-circuits to 503 ("not configured") before
/// any GitHub call — which also lets the rate-limit test run without hitting the network.
/// <para>
/// The feedback limiter is always on (not gated on internet mode), so the 429 test does NOT
/// enable internet mode — proving the limiter engages on the LAN too.
/// </para>
/// </summary>
public class FeedbackIntegrationTests
{
    private static object ValidBody() => new
    {
        summary = "AI DM stopped responding after rolling initiative",
        description = "I rolled initiative and the AI never continued narrating.",
    };

    [Fact]
    public async Task FeedbackController_NoTokenConfigured_Returns503WithClearMessage()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/feedback", ValidBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Feedback is not configured on this instance.", payload!.Error);
    }

    [Fact]
    public async Task FeedbackController_MissingSummary_Returns400()
    {
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/feedback",
            new { description = "no summary here" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FeedbackController_RateLimit_Returns429AfterFiveRequests()
    {
        // No internet mode enabled — proves the feedback limiter is always on.
        using var factory = new AircaneWebApplicationFactory();
        var client = factory.CreateClient();

        // Limit is 5/hour per IP. The test client shares one IP, so the 6th request is shed with
        // 429 even though the first 5 return 503 (not configured) from the handler.
        HttpStatusCode? lastStatus = null;
        var saw429 = false;
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/feedback", ValidBody());
            lastStatus = response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                saw429 = true;
                Assert.True(
                    response.Headers.Contains("Retry-After"),
                    "429 response should include a Retry-After header.");
                break;
            }
        }

        Assert.True(saw429, $"Expected a 429 within 8 rapid requests; last status was {lastStatus}.");
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}
