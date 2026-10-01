using Aircane.Api.Controllers;
using Aircane.Application.Feedback;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Aircane.UnitTests.Feedback;

/// <summary>
/// Unit tests for <see cref="FeedbackController"/> using a hand-written stub service (no Moq,
/// matching the repo's test style).
/// </summary>
public class FeedbackControllerTests
{
    private static FeedbackDto ValidDto() => new()
    {
        Summary = "AI DM stopped responding after rolling initiative",
        Description = "I rolled initiative and the AI never continued narrating.",
    };

    [Fact]
    public async Task FeedbackController_ValidRequest_CreatesGitHubIssue()
    {
        var stub = new StubFeedbackService
        {
            Result = new FeedbackResult
            {
                Success = true,
                IssueUrl = "https://github.com/owner/aircane-tabletop/issues/42",
                IssueNumber = 42,
            },
        };
        var controller = new FeedbackController(stub);

        var response = await controller.Submit(ValidDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.True(stub.WasCalled);

        // Reflect over the anonymous response object to assert its shape.
        var payload = ok.Value!;
        Assert.Equal(true, GetProp(payload, "success"));
        Assert.Equal("https://github.com/owner/aircane-tabletop/issues/42", GetProp(payload, "issueUrl"));
        Assert.Equal(42, GetProp(payload, "issueNumber"));
    }

    [Fact]
    public async Task FeedbackController_MissingSummary_Returns400()
    {
        var stub = new StubFeedbackService();
        var controller = new FeedbackController(stub);

        var dto = ValidDto();
        dto.Summary = "   ";

        var response = await controller.Submit(dto, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.False(stub.WasCalled);
    }

    [Fact]
    public async Task FeedbackController_MissingDescription_Returns400()
    {
        var stub = new StubFeedbackService();
        var controller = new FeedbackController(stub);

        var dto = ValidDto();
        dto.Description = null;

        var response = await controller.Submit(dto, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.False(stub.WasCalled);
    }

    [Fact]
    public async Task FeedbackController_NoTokenConfigured_Returns503WithClearMessage()
    {
        var stub = new StubFeedbackService
        {
            ExceptionToThrow = new FeedbackNotConfiguredException(),
        };
        var controller = new FeedbackController(stub);

        var response = await controller.Submit(ValidDto(), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(response);
        Assert.Equal(503, status.StatusCode);
        Assert.Equal("Feedback is not configured on this instance.", GetProp(status.Value!, "error"));
    }

    private static object? GetProp(object obj, string name) =>
        obj.GetType().GetProperty(name)!.GetValue(obj);

    private sealed class StubFeedbackService : IFeedbackService
    {
        public FeedbackResult Result { get; set; } = new() { Success = true };
        public Exception? ExceptionToThrow { get; set; }
        public bool WasCalled { get; private set; }

        public Task<FeedbackResult> SubmitAsync(FeedbackDto dto, CancellationToken ct)
        {
            WasCalled = true;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;
            return Task.FromResult(Result);
        }
    }
}
