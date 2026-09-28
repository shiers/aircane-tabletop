namespace Aircane.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Lifecycle state of a background job tracked by <see cref="IBackgroundJobStatusStore"/>.
/// </summary>
public enum BackgroundJobState
{
    /// <summary>The job has been enqueued but not yet picked up by the worker.</summary>
    Queued = 0,

    /// <summary>The worker has dequeued the job and is executing its handler.</summary>
    Running = 1,

    /// <summary>The handler completed successfully.</summary>
    Completed = 2,

    /// <summary>The handler threw or the job could not be processed. See error message.</summary>
    Failed = 3,
}

/// <summary>
/// A snapshot of a background job's tracked status. Returned by <c>GET /api/jobs/{id}</c>.
/// </summary>
public sealed record BackgroundJobStatus(
    Guid JobId,
    string JobType,
    Guid? DocumentId,
    BackgroundJobState State,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    int? ProgressPercent);
