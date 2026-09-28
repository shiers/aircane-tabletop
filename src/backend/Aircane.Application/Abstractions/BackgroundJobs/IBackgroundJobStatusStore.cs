namespace Aircane.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Tracks the lifecycle status and progress of background jobs so the host UI can poll
/// (<c>GET /api/jobs/{id}</c>) or correlate SignalR progress events to a specific job.
/// </summary>
/// <remarks>
/// The MVP implementation is an in-memory store (bounded, self-evicting). If persistent job
/// history is required later, this interface can be backed by a <c>BackgroundJob</c> table
/// without changing callers.
/// </remarks>
public interface IBackgroundJobStatusStore
{
    /// <summary>Records a newly enqueued job in the <see cref="BackgroundJobState.Queued"/> state.</summary>
    void Register(IBackgroundJob job);

    /// <summary>Marks a job as <see cref="BackgroundJobState.Running"/> and stamps its start time.</summary>
    void MarkRunning(Guid jobId);

    /// <summary>Updates the progress percentage (0–100) of a running job, if tracked.</summary>
    void ReportProgress(Guid jobId, int progressPercent);

    /// <summary>Marks a job as <see cref="BackgroundJobState.Completed"/> and stamps its completion time.</summary>
    void MarkCompleted(Guid jobId);

    /// <summary>Marks a job as <see cref="BackgroundJobState.Failed"/> with an error message.</summary>
    void MarkFailed(Guid jobId, string errorMessage);

    /// <summary>Returns the current status of a job, or null if unknown/evicted.</summary>
    BackgroundJobStatus? Get(Guid jobId);
}
