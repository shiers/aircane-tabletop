namespace Aircane.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Marker/base contract for a unit of background work that can be enqueued on the
/// <see cref="IBackgroundJobQueue"/> and dispatched to a matching <see cref="IJobHandler{T}"/>.
/// </summary>
/// <remarks>
/// Jobs are intentionally lightweight message records. They carry only the identifiers needed
/// to reconstitute the work inside a fresh DI scope on the worker — never live services or
/// entity instances.
/// </remarks>
public interface IBackgroundJob
{
    /// <summary>
    /// The unique identifier assigned to this job when it is enqueued. Used to correlate
    /// status/progress updates (see <see cref="IBackgroundJobStatusStore"/>) and SignalR events.
    /// </summary>
    Guid JobId { get; }

    /// <summary>
    /// A stable, human-readable job type discriminator (e.g. "DocumentImport", "FolderScan",
    /// "Reembed"). Used for status reporting and diagnostics.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// The document this job relates to, when applicable. Null for jobs that are not
    /// scoped to a single document (e.g. re-embed-all, folder scan).
    /// </summary>
    Guid? DocumentId { get; }
}
