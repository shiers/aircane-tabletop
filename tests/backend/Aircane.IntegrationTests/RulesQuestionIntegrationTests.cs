using System.Net;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Ai;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the rules question workflow.
/// Verifies: POST rules question → response has answer (using fake AI provider).
/// </summary>
public class RulesQuestionIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RulesQuestionIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AskRulesQuestion_ReturnsAnswerWithCitations()
    {
        // Arrange
        var request = new RulesQuestionRequest(
            Question: "How does advantage work in D&D 5e?",
            GameSystem: "D&D 5e 2014",
            Ruleset: "PHB");

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/rules-question", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RulesQuestionResponse>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Answer));
        // The fake AI provider should still return a structured response
        Assert.NotNull(result.Citations);
    }

    [Fact]
    public async Task AskRulesQuestion_WithEmptyQuestion_ReturnsBadRequest()
    {
        // Arrange
        var request = new RulesQuestionRequest(Question: "");

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/rules-question", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AskRulesQuestion_WithNoSources_IndicatesLackOfSupport()
    {
        // Arrange — no documents have been imported, so retrieval should find nothing
        var request = new RulesQuestionRequest(
            Question: "What is the range of a fireball spell?",
            GameSystem: "D&D 5e 2014");

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/rules-question", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RulesQuestionResponse>();
        Assert.NotNull(result);
        // With no indexed sources, HasSourceSupport should be false
        Assert.False(result.HasSourceSupport);
    }
}
