namespace Aircane.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Handles a specific type of <see cref="IBackgroundJob"/>. Registered per job type and
/// resolved by the worker inside a fresh DI scope when a matching job is dequeued.
/// </summary>
/// <typeparam name="TJob">The concrete job type this handler processes.</typeparam>
public interface IJobHandler<in TJob>
    where TJob : IBackgroundJob
{
    /// <summary>
    /// Executes the work for the given job. Implementations should be self-contained and
    /// resolve any needed scoped services via constructor injection.
    /// </summary>
    Task HandleAsync(TJob job, CancellationToken ct);
}
