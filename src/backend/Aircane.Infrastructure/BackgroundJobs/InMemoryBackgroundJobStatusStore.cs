using System.Collections.Concurrent;
using Aircane.Application.Abstractions.BackgroundJobs;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory, thread-safe <see cref="IBackgroundJobStatusStore"/>. Suitable for the local-first
/// MVP where job history need not survive a restart.
/// </summary>
/// <remarks>
/// The store is bounded: once the number of tracked jobs exceeds <see cref="MaxEntries"/>, the
/// oldest completed/failed entries are evicted. Running/queued jobs are never evicted.
/// </remarks>
public sealed class InMemoryBackgroundJobStatusStore : IBackgroundJobStatusStore
{
    /// <summary>Maximum number of job status entries retained before eviction.</summary>
    public const int MaxEntries = 500;

    private readonly ConcurrentDictionary<Guid, BackgroundJobStatus> _jobs = new();

    /// <inheritdoc />
    public void Register(IBackgroundJob job)
    {
        var now = DateTimeOffset.UtcNow;
        _jobs[job.JobId] = new BackgroundJobStatus(
            JobId: job.JobId,
            JobType: job.JobType,
            DocumentId: job.DocumentId,
            State: BackgroundJobState.Queued,
            EnqueuedAt: now,
            StartedAt: null,
            CompletedAt: null,
            ErrorMessage: null,
            ProgressPercent: null);

        EvictIfNeeded();
    }

    /// <inheritdoc />
    public void MarkRunning(Guid jobId) =>
        Update(jobId, s => s with { State = BackgroundJobState.Running, StartedAt = DateTimeOffset.UtcNow });

    /// <inheritdoc />
    public void ReportProgress(Guid jobId, int progressPercent) =>
        Update(jobId, s => s with { ProgressPercent = Math.Clamp(progressPercent, 0, 100) });

    /// <inheritdoc />
    public void MarkCompleted(Guid jobId) =>
        Update(jobId, s => s with
        {
            State = BackgroundJobState.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            ProgressPercent = 100,
        });

    /// <inheritdoc />
    public void MarkFailed(Guid jobId, string errorMessage) =>
        Update(jobId, s => s with
        {
            State = BackgroundJobState.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage,
        });

    /// <inheritdoc />
    public BackgroundJobStatus? Get(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var status) ? status : null;

    private void Update(Guid jobId, Func<BackgroundJobStatus, BackgroundJobStatus> mutate)
    {
        // If the entry was evicted, silently no-op rather than resurrecting a phantom job.
        if (_jobs.TryGetValue(jobId, out var existing))
            _jobs.TryUpdate(jobId, mutate(existing), existing);
    }

    private void EvictIfNeeded()
    {
        if (_jobs.Count <= MaxEntries)
            return;

        var evictable = _jobs.Values
            .Where(s => s.State is BackgroundJobState.Completed or BackgroundJobState.Failed)
            .OrderBy(s => s.CompletedAt ?? s.EnqueuedAt)
            .Take(_jobs.Count - MaxEntries)
            .Select(s => s.JobId)
            .ToList();

        foreach (var id in evictable)
            _jobs.TryRemove(id, out _);
    }
}
