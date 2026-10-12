using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.DTOs.Adventures;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs adventure generation off the request thread and reports each stage over SignalR.
/// Failures are announced to clients and then rethrown so the worker records the job as failed.
/// </summary>
public sealed class AdventureGenerationJobHandler : IJobHandler<AdventureGenerationJobMessage>
{
    private readonly IAdventureGenerationService _generation;
    private readonly IPartyAnalysisService _partyAnalysis;
    private readonly IAdventureGenerationNotifier _notifier;
    private readonly IBackgroundJobStatusStore _statusStore;
    private readonly ILogger<AdventureGenerationJobHandler> _logger;

    public AdventureGenerationJobHandler(
        IAdventureGenerationService generation,
        IPartyAnalysisService partyAnalysis,
        IAdventureGenerationNotifier notifier,
        IBackgroundJobStatusStore statusStore,
        ILogger<AdventureGenerationJobHandler> logger)
    {
        _generation = generation;
        _partyAnalysis = partyAnalysis;
        _notifier = notifier;
        _statusStore = statusStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(AdventureGenerationJobMessage job, CancellationToken ct)
    {
        try
        {
            Report(job.JobId, AdventureGenerationStages.Analysing, ct);

            PartyAnalysisResult? partyAnalysis = null;
            if (job.Request.CharacterIds is { Count: > 0 })
            {
                try
                {
                    partyAnalysis = await _partyAnalysis.AnalyzePartyAsync(job.Request.CharacterIds, ct);
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Party analysis failed, proceeding without it");
                }
            }

            var adventure = await _generation.GenerateAsync(
                job.Request, partyAnalysis, ct, (stage, _) => Report(job.JobId, stage, ct), job.CampaignId);

            Report(job.JobId, AdventureGenerationStages.Complete, ct);
            await _notifier.NotifyCompletedAsync(new AdventureGenerationCompletedNotification(
                job.JobId.ToString(), adventure.Id.ToString(), adventure.Pitch?.Title ?? "Untitled adventure"), ct);

            _logger.LogInformation(
                "Adventure generation job {JobId} produced adventure {AdventureId}", job.JobId, adventure.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown: not a failure to report
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adventure generation job {JobId} failed", job.JobId);
            await SafeNotifyFailedAsync(job.JobId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Progress is best effort: a missed update must never fail generation. The generation service's
    /// callback is synchronous, and the worker has no synchronization context, so waiting is safe.
    /// </summary>
    private void Report(Guid jobId, string stage, CancellationToken ct)
    {
        var percent = AdventureGenerationStages.PercentFor(stage);
        _statusStore.ReportProgress(jobId, percent);

        try
        {
            _notifier.NotifyProgressAsync(new AdventureGenerationProgressNotification(
                jobId.ToString(), stage, percent, AdventureGenerationStages.MessageFor(stage)), ct)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not send generation progress for job {JobId}", jobId);
        }
    }

    private async Task SafeNotifyFailedAsync(Guid jobId, string error)
    {
        try
        {
            await _notifier.NotifyFailedAsync(new AdventureGenerationFailedNotification(jobId.ToString(), error));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send generation failure for job {JobId}", jobId);
        }
    }
}
