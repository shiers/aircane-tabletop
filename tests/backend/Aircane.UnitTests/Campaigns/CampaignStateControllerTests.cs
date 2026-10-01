using Aircane.Api.Controllers;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.CampaignState;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Campaigns;

/// <summary>
/// Unit tests for <see cref="CampaignStateController"/>, focusing on the undo endpoint.
/// Uses a simple stub implementation of <see cref="ICampaignStateService"/>.
/// </summary>
public class CampaignStateControllerTests
{
    private readonly CampaignStateController _controller;
    private readonly StubCampaignStateService _stub;

    public CampaignStateControllerTests()
    {
        _stub = new StubCampaignStateService();
        _controller = new CampaignStateController(
            _stub,
            NullLogger<CampaignStateController>.Instance);
    }

    // ── POST state/undo ───────────────────────────────────────────────────────

    [Fact]
    public async Task UndoLastCommand_Success_ReturnsOkWithRestoredState()
    {
        var campaignId = Guid.NewGuid();
        var expectedState = new CampaignStateDto(
            CampaignId: campaignId,
            ActiveSessionId: null,
            CurrentSceneJson: "village",
            StateJson: """{"currentSceneId":"village"}""",
            SnapshotAt: DateTimeOffset.UtcNow);

        _stub.UndoResult = expectedState;

        var result = await _controller.UndoLastCommand(campaignId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var state = Assert.IsType<CampaignStateDto>(okResult.Value);
        Assert.Equal(campaignId, state.CampaignId);
        Assert.Equal("village", state.CurrentSceneJson);
    }

    [Fact]
    public async Task UndoLastCommand_CampaignNotFound_Returns404()
    {
        _stub.UndoException = new KeyNotFoundException("Campaign not found.");

        var result = await _controller.UndoLastCommand(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UndoLastCommand_NoReversibleCommands_Returns409Conflict()
    {
        _stub.UndoException = new InvalidOperationException("No reversible commands to undo.");

        var result = await _controller.UndoLastCommand(Guid.NewGuid(), CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);
    }

    [Fact]
    public async Task UndoLastCommand_BeforeStateUnavailable_Returns409Conflict()
    {
        _stub.UndoException = new InvalidOperationException(
            "Cannot undo: before-state not available in event payload.");

        var result = await _controller.UndoLastCommand(Guid.NewGuid(), CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);
    }

    [Fact]
    public async Task UndoLastCommand_ConflictResponse_ContainsErrorMessage()
    {
        var errorMessage = "No reversible commands to undo.";
        _stub.UndoException = new InvalidOperationException(errorMessage);

        var result = await _controller.UndoLastCommand(Guid.NewGuid(), CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        // The response body should contain the error message
        var json = System.Text.Json.JsonSerializer.Serialize(conflictResult.Value);
        Assert.Contains("No reversible commands to undo", json);
    }

    // ── Stub Implementation ───────────────────────────────────────────────────

    private sealed class StubCampaignStateService : ICampaignStateService
    {
        public CampaignStateDto? UndoResult { get; set; }
        public Exception? UndoException { get; set; }

        public Task<CampaignStateDto> UndoLastCommandAsync(
            Guid campaignId, CancellationToken cancellationToken = default)
        {
            if (UndoException is not null)
                throw UndoException;

            return Task.FromResult(UndoResult!);
        }

        // ── Other interface methods (not under test) ──────────────────────────

        public Task<CampaignStateDto> LoadStateAsync(
            Guid campaignId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CampaignStateDto> ApplyCommandAsync(
            ApplyCommandRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CampaignEventDto> AppendEventAsync(
            AppendEventRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<CampaignEventDto>> GetEventLogAsync(
            Guid campaignId, int page = 1, int pageSize = 50,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CampaignStateDto> ReplayEventsAsync(
            Guid campaignId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
