using System.Security.Claims;
using Aircane.Api.Controllers;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Persistence;
using Aircane.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

/// <summary>Coming back to a run: stale pending rolls are dropped, and the AI writes a "story so far".</summary>
public sealed class AdventureResumeTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly RecapAiProvider _ai = new();
    private readonly AdventureSessionService _service;
    private readonly AdventureSessionsController _controller;
    private Campaign _campaign = null!;
    private GeneratedAdventure _adventure = null!;
    private Guid _runId;

    public AdventureResumeTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new AdventureSessionService(
            _db,
            new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance,
            hubNotifier: null,
            aiProvider: _ai);
        _controller = new AdventureSessionsController(
            _service, playerActionService: null!, sessionHosting: null!, NullLogger<AdventureSessionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = LocalHostHttpContext.Create() },
        };
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedRunAsync()
    {
        _campaign = new Campaign("C", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _adventure = AdventureSessionTestData.CreateAdventure();
        _db.Campaigns.Add(_campaign);
        _db.GeneratedAdventures.Add(_adventure);
        await _db.SaveChangesAsync();
        _runId = (await _service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null)).Id;
    }

    private Task<AdventureSession> Run() => _db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == _runId);

    // ── Stale pending roll ────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSession_ClearsPendingRoll_OnResume()
    {
        await SeedRunAsync();
        await _service.UpdatePendingRollAsync(_runId, "Dexterity (Stealth)", 14);

        // Starting an adventure that is already running is a resume...
        var resumed = await _service.StartAdventureSessionAsync(_adventure.Id, _campaign.Id, null);

        Assert.Equal(_runId, resumed.Id);   // same run, not a new one
        var run = await Run();
        Assert.Null(run.PendingRollLabel);
        Assert.Null(run.PendingRollDC);
    }

    [Fact]
    public async Task AdventureSessionsController_Resume_ClearsPendingRoll()
    {
        // ... and so is opening a run directly (the Resume buttons), via the resume endpoint.
        await SeedRunAsync();
        await _service.UpdatePendingRollAsync(_runId, "Athletics", 12);

        Assert.IsType<NoContentResult>(await _controller.Resume(_runId, default));

        var run = await Run();
        Assert.Null(run.PendingRollLabel);
        Assert.Null(run.PendingRollDC);
    }

    [Fact]
    public async Task Resume_WithNothingPending_ChangesNothing()
    {
        await SeedRunAsync();
        var before = (await Run()).LastActivityAt;

        await _service.ResumeAsync(_runId);

        Assert.Equal(before, (await Run()).LastActivityAt);
        Assert.IsType<NotFoundResult>(await _controller.Resume(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Resume_KeepsEverythingElse()
    {
        await SeedRunAsync();
        await _service.RevealClueAsync(_runId, "clue-1");
        await _service.AdvanceSceneAsync(_runId, "scene-2", null);
        await _service.UpdatePendingRollAsync(_runId, "Perception", null);

        await _service.ResumeAsync(_runId);

        var run = await Run();
        Assert.Equal("scene-2", run.CurrentSceneId);
        Assert.Equal(["clue-1"], run.RevealedClueIds);
        Assert.Equal(["scene-1"], run.CompletedSceneIds);
    }

    // ── Recap ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdventureSessionsController_Recap_ReturnsAiGeneratedSummary()
    {
        await SeedRunAsync();
        _ai.Reply = "  \"You are in the Sunken Library, hunting for the missing tome.\"  ";

        var result = await _controller.Recap(_runId, default);

        var body = Assert.IsType<AdventureRecapDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("You are in the Sunken Library, hunting for the missing tome.", body.Recap);   // trimmed, unquoted
        Assert.Equal(1, _ai.Calls);
    }

    [Fact]
    public async Task AdventureSessionsController_Recap_IncludesCompletedScenesAndClues()
    {
        await SeedRunAsync();
        await _service.RevealClueAsync(_runId, "clue-1");                 // "The torn letter"
        await _service.AdvanceSceneAsync(_runId, "scene-2", null);        // completes "The Sunken Library"
        await _service.RevealSecretAsync(_runId, "secret-1");             // "The Vault Key"

        await _controller.Recap(_runId, default);

        var prompt = _ai.LastUserPrompt;
        Assert.Contains("2-3 sentences", prompt);
        Assert.Contains("second person present tense", prompt);
        Assert.Contains("Adventure: The Sunken Archive", prompt);
        Assert.Contains("Completed scenes: The Sunken Library", prompt);
        Assert.Contains("Revealed clues: The torn letter", prompt);
        Assert.Contains("Revealed secrets: The Vault Key", prompt);
        Assert.Contains("Current scene: The Grand Hall - A vaulted hall of cracked marble.", prompt);
        Assert.Contains("Current objectives: none listed", prompt);   // scene-2 has none
    }

    [Fact]
    public async Task Recap_ListsObjectives_AndSaysNoneYetForAFreshRun()
    {
        await SeedRunAsync();

        await _controller.Recap(_runId, default);

        var prompt = _ai.LastUserPrompt;
        Assert.Contains("Completed scenes: none yet", prompt);
        Assert.Contains("Revealed clues: none yet", prompt);
        Assert.Contains("Revealed secrets: none yet", prompt);
        Assert.Contains("Current objectives: Find the missing tome; Deal with the guard", prompt);
    }

    [Fact]
    public async Task Recap_IsASmallFocusedCall_NotTheDmPrompt()
    {
        await SeedRunAsync();

        await _controller.Recap(_runId, default);

        // One short system line + the recap request: no DM instructions, retrieved rules or action schema.
        Assert.Equal(2, _ai.LastMessageCount);
        Assert.DoesNotContain("RETRIEVED", _ai.LastUserPrompt);
        Assert.DoesNotContain("proposedActions", _ai.LastUserPrompt);
        Assert.DoesNotContain("DM notes", _ai.LastUserPrompt);   // no hidden scene notes leak into the reminder
    }

    [Fact]
    public async Task Recap_AiFailureOrEmptyReply_Returns400_NotA500()
    {
        await SeedRunAsync();
        _ai.Reply = "   ";

        Assert.IsType<BadRequestObjectResult>(await _controller.Recap(_runId, default));

        _ai.Throw = new InvalidOperationException("OpenAI returned an error");
        Assert.IsType<BadRequestObjectResult>(await _controller.Recap(_runId, default));
    }

    [Fact]
    public async Task Recap_UnknownRun_Returns404_AndAnotherPlayersRun_IsForbidden()
    {
        await SeedRunAsync();
        Assert.IsType<NotFoundResult>(await _controller.Recap(Guid.NewGuid(), default));

        _controller.ControllerContext.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Player"), new Claim("sid", Guid.NewGuid().ToString())], "Test"));
        Assert.IsType<ForbidResult>(await _controller.Recap(_runId, default));
        Assert.Equal(0, _ai.Calls);   // nothing was spent on a forbidden request
    }

    [Fact]
    public async Task Recap_WithoutAnAiProvider_ExplainsWhy()
    {
        await SeedRunAsync();
        var noAi = new AdventureSessionService(
            _db, new AdventureRetrievalService(_db, NullLogger<AdventureRetrievalService>.Instance),
            new CampaignStateService(_db, NullLogger<CampaignStateService>.Instance),
            NullLogger<AdventureSessionService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => noAi.GenerateRecapAsync(_runId));
        Assert.Contains("No AI provider", ex.Message);
    }

    private sealed class RecapAiProvider : IAiProvider
    {
        public string ProviderName => "Recap";
        public string Reply { get; set; } = "You are somewhere.";
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public int LastMessageCount { get; private set; }
        public string LastUserPrompt { get; private set; } = "";

        public Task<string> ChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
        {
            Calls++;
            LastMessageCount = messages.Count;
            LastUserPrompt = messages.Last(m => m.Role == "user").Content;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Reply);
        }

        public Task<AiStructuredOutput> StructuredChatCompletionAsync(IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
