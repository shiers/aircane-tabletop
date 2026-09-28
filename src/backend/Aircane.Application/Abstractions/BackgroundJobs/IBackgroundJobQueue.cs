namespace Aircane.Application.Abstractions.BackgroundJobs;

/// <summary>
/// An in-process queue of background jobs. Callers (controllers, services) enqueue a job and
/// return immediately; a hosted worker dequeues and dispatches each job to its handler.
/// </summary>
/// <remarks>
/// The default implementation is a <c>Channel&lt;IBackgroundJob&gt;</c>-backed singleton
/// (<c>ChannelBackgroundJobQueue</c>). A persistent runner (e.g. Hangfire) can be swapped in
/// later behind this same interface without touching callers — see the <c>BackgroundJobs</c>
/// feature-flag branch in Infrastructure DI.
/// </remarks>
public interface IBackgroundJobQueue
{
    /// <summary>
    /// Enqueues a job for background processing. Returns as soon as the job is accepted onto
    /// the queue; the work itself runs on the worker.
    /// </summary>
    ValueTask EnqueueAsync<T>(T job, CancellationToken ct = default)
        where T : IBackgroundJob;

    /// <summary>
    /// Dequeues the next job, waiting asynchronously until one is available or the token is
    /// cancelled. Called by the worker loop.
    /// </summary>
    ValueTask<IBackgroundJob> DequeueAsync(CancellationToken ct);
}
