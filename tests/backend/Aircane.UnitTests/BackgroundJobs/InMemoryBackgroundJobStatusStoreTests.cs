using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Infrastructure.BackgroundJobs;
using Xunit;

namespace Aircane.UnitTests.BackgroundJobs;

/// <summary>
/// Unit tests for <see cref="InMemoryBackgroundJobStatusStore"/> lifecycle transitions.
/// </summary>
public class InMemoryBackgroundJobStatusStoreTests
{
    [Fact]
    public void Register_Then_Get_ReturnsQueued()
    {
        var store = new InMemoryBackgroundJobStatusStore();
        var job = new DocumentImportJobMessage(Guid.NewGuid());

        store.Register(job);

        var status = store.Get(job.JobId);
        Assert.NotNull(status);
        Assert.Equal(BackgroundJobState.Queued, status!.State);
        Assert.Null(status.StartedAt);
        Assert.Null(status.CompletedAt);
    }

    [Fact]
    public void MarkRunning_Then_MarkCompleted_UpdatesStateAndTimestamps()
    {
        var store = new InMemoryBackgroundJobStatusStore();
        var job = new ReembedJobMessage(null);
        store.Register(job);

        store.MarkRunning(job.JobId);
        var running = store.Get(job.JobId)!;
        Assert.Equal(BackgroundJobState.Running, running.State);
        Assert.NotNull(running.StartedAt);

        store.MarkCompleted(job.JobId);
        var completed = store.Get(job.JobId)!;
        Assert.Equal(BackgroundJobState.Completed, completed.State);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(100, completed.ProgressPercent);
    }

    [Fact]
    public void MarkFailed_RecordsErrorMessage()
    {
        var store = new InMemoryBackgroundJobStatusStore();
        var job = new FolderScanJobMessage(Guid.NewGuid());
        store.Register(job);

        store.MarkFailed(job.JobId, "boom");

        var failed = store.Get(job.JobId)!;
        Assert.Equal(BackgroundJobState.Failed, failed.State);
        Assert.Equal("boom", failed.ErrorMessage);
    }

    [Fact]
    public void ReportProgress_ClampsToValidRange()
    {
        var store = new InMemoryBackgroundJobStatusStore();
        var job = new DocumentImportJobMessage(Guid.NewGuid());
        store.Register(job);

        store.ReportProgress(job.JobId, 150);
        Assert.Equal(100, store.Get(job.JobId)!.ProgressPercent);

        store.ReportProgress(job.JobId, -5);
        Assert.Equal(0, store.Get(job.JobId)!.ProgressPercent);
    }

    [Fact]
    public void Get_UnknownJob_ReturnsNull()
    {
        var store = new InMemoryBackgroundJobStatusStore();
        Assert.Null(store.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Update_OnEvictedOrUnknownJob_DoesNotThrow()
    {
        var store = new InMemoryBackgroundJobStatusStore();
        // No Register call; marking transitions on an unknown id must be a silent no-op.
        store.MarkRunning(Guid.NewGuid());
        store.MarkCompleted(Guid.NewGuid());
        store.MarkFailed(Guid.NewGuid(), "x");
    }
}
