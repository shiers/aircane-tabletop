using Aircane.Api.Controllers;
using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.DTOs.Adventures;
using Aircane.Application.Validation;
using Aircane.Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class AdventureGenerationJobTests
{
    private static readonly string[] Stages = ["pitch", "outline", "scenes", "npcs", "encounters", "clues"];

    private static GenerateAdventureRequest Request() => new()
    {
        Mode = "Solo", Ruleset = "D&D 5e 2014", GameSystem = "D&D 5e 2014", Tone = "epic",
        Length = "one-shot", Difficulty = "medium", PartySize = 4, AverageLevel = 5,
        CombatRatio = 40, ExplorationRatio = 30, RoleplayRatio = 30,
    };

    private sealed class FakeGeneration : IAdventureGenerationService
    {
        public Exception? Throw { get; set; }
        public Guid AdventureId { get; } = Guid.NewGuid();

        public Task<GeneratedAdventureDto> GenerateAsync(
            GenerateAdventureRequest request, PartyAnalysisResult? partyAnalysis,
            CancellationToken cancellationToken = default, Action<string, int>? onProgress = null, Guid? campaignId = null)
        {
            foreach (var stage in Stages)
                onProgress?.Invoke(stage, AdventureGenerationStages.PercentFor(stage));
            if (Throw is not null) throw Throw;
            return Task.FromResult(new GeneratedAdventureDto
            {
                Id = AdventureId, Status = "Draft", Request = request,
                Pitch = new AdventurePitch { Title = "The Sunken Archive", Hook = "h", Summary = "s" },
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }

        public Task<AdventurePitch> RegeneratePitchAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AdventureOutline> RegenerateOutlineAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, AdventurePitch pi, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedScene>> RegenerateScenesAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, AdventurePitch pi, AdventureOutline o, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedNpc>> RegenerateNpcsAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, AdventurePitch pi, IReadOnlyList<GeneratedScene> s, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedEncounter>> RegenerateEncountersAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, AdventurePitch pi, IReadOnlyList<GeneratedScene> s, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AdventureTreasure> RegenerateTreasureAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, AdventurePitch pi, IReadOnlyList<GeneratedScene> s, IReadOnlyList<GeneratedEncounter> e, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AdventureClues> RegenerateCluesAsync(GenerateAdventureRequest r, PartyAnalysisResult? p, AdventurePitch pi, IReadOnlyList<GeneratedScene> s, IReadOnlyList<GeneratedNpc> n, CancellationToken c = default) => throw new NotSupportedException();
    }

    private sealed class FakeNotifier : IAdventureGenerationNotifier
    {
        public List<AdventureGenerationProgressNotification> Progress { get; } = [];
        public List<AdventureGenerationCompletedNotification> Completed { get; } = [];
        public List<AdventureGenerationFailedNotification> Failed { get; } = [];
        public Task NotifyProgressAsync(AdventureGenerationProgressNotification n, CancellationToken ct = default) { Progress.Add(n); return Task.CompletedTask; }
        public Task NotifyCompletedAsync(AdventureGenerationCompletedNotification n, CancellationToken ct = default) { Completed.Add(n); return Task.CompletedTask; }
        public Task NotifyFailedAsync(AdventureGenerationFailedNotification n, CancellationToken ct = default) { Failed.Add(n); return Task.CompletedTask; }
    }

    private sealed class NoParty : IPartyAnalysisService
    {
        public Task<PartyAnalysisResult> AnalyzePartyAsync(IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static (AdventureGenerationJobHandler Handler, FakeGeneration Gen, FakeNotifier Notifier) Create()
    {
        var gen = new FakeGeneration();
        var notifier = new FakeNotifier();
        return (new AdventureGenerationJobHandler(gen, new NoParty(), notifier, new InMemoryBackgroundJobStatusStore(),
            NullLogger<AdventureGenerationJobHandler>.Instance), gen, notifier);
    }

    [Fact]
    public async Task AdventureGenerationJob_EmitsProgressEvents_AtEachStage()
    {
        var (handler, _, notifier) = Create();
        var job = new AdventureGenerationJobMessage(Request(), null);

        await handler.HandleAsync(job, default);

        Assert.Equal(
            [("analysing", 10), ("pitch", 20), ("outline", 35), ("scenes", 55), ("npcs", 70), ("encounters", 82), ("clues", 92), ("complete", 100)],
            notifier.Progress.Select(p => (p.Stage, p.PercentComplete)));
        Assert.All(notifier.Progress, p => Assert.Equal(job.JobId.ToString(), p.JobId));
        Assert.Equal("Writing scenes and locations...", notifier.Progress.Single(p => p.Stage == "scenes").Message);
        Assert.Equal("Adventure ready!", notifier.Progress[^1].Message);
    }

    [Fact]
    public async Task AdventureGenerationJob_EmitsCompletedEvent_OnSuccess()
    {
        var (handler, gen, notifier) = Create();
        var job = new AdventureGenerationJobMessage(Request(), null);

        await handler.HandleAsync(job, default);

        var done = Assert.Single(notifier.Completed);
        Assert.Equal(job.JobId.ToString(), done.JobId);
        Assert.Equal(gen.AdventureId.ToString(), done.AdventureId);
        Assert.Equal("The Sunken Archive", done.AdventureTitle);
        Assert.Empty(notifier.Failed);
    }

    [Fact]
    public async Task AdventureGenerationJob_EmitsFailedEvent_OnException()
    {
        var (handler, gen, notifier) = Create();
        gen.Throw = new InvalidOperationException("Failed to parse AI response for stage 'scenes'.");
        var job = new AdventureGenerationJobMessage(Request(), null);

        // Rethrown so the worker marks the job failed.
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job, default));

        var failed = Assert.Single(notifier.Failed);
        Assert.Equal(job.JobId.ToString(), failed.JobId);
        Assert.Contains("stage 'scenes'", failed.Error);
        Assert.Empty(notifier.Completed);
    }

    private static AdventuresController Controller(IBackgroundJobQueue queue) => new(
        new GenerateAdventureValidator(), retrievalService: null!, adventureIndexingService: null!,
        jobQueue: queue, dbContext: null!, logger: NullLogger<AdventuresController>.Instance);

    [Fact]
    public async Task AdventuresController_GenerateAdventure_Returns202WithJobId()
    {
        var queue = new ChannelBackgroundJobQueue(new InMemoryBackgroundJobStatusStore());

        var result = await Controller(queue).GenerateAdventure(Request(), null, default);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<GenerateAdventureResponse>(accepted.Value);
        Assert.NotEqual(Guid.Empty, body.JobId);
        var queued = Assert.IsType<AdventureGenerationJobMessage>(await queue.DequeueAsync(default));
        Assert.Equal(body.JobId, queued.JobId);
    }

    [Fact]
    public async Task AdventuresController_GenerateAdventure_DoesNotBlock()
    {
        // Nothing consumes the queue and no generation service is wired in: the action must return
        // promptly because all the work happens in the background job.
        var queue = new ChannelBackgroundJobQueue(new InMemoryBackgroundJobStatusStore());

        var task = Controller(queue).GenerateAdventure(Request(), null, default);

        Assert.Same(task, await Task.WhenAny(task, Task.Delay(2000)));
        Assert.IsType<AcceptedResult>(await task);
    }

    [Fact]
    public async Task AdventuresController_GenerateAdventure_InvalidRequest_Returns400AndEnqueuesNothing()
    {
        var queue = new ChannelBackgroundJobQueue(new InMemoryBackgroundJobStatusStore());
        var bad = Request() with { PartySize = 0 };

        var result = await Controller(queue).GenerateAdventure(bad, null, default);

        Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queue.DequeueAsync(cts.Token));
    }
}
