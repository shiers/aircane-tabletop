using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Infrastructure.BackgroundJobs;
using Xunit;

namespace Aircane.UnitTests.BackgroundJobs;

/// <summary>
/// Unit tests for <see cref="ChannelBackgroundJobQueue"/>: FIFO ordering, enqueue-registers-status,
/// and cancellation behaviour on dequeue.
/// </summary>
public class ChannelBackgroundJobQueueTests
{
    private static ChannelBackgroundJobQueue CreateQueue(out InMemoryBackgroundJobStatusStore store)
    {
        store = new InMemoryBackgroundJobStatusStore();
        return new ChannelBackgroundJobQueue(store);
    }

    [Fact]
    public async Task Enqueue_Then_Dequeue_PreservesFifoOrder()
    {
        var queue = CreateQueue(out _);

        var first = new DocumentImportJobMessage(Guid.NewGuid());
        var second = new FolderScanJobMessage(Guid.NewGuid());
        var third = new ReembedJobMessage(null);

        await queue.EnqueueAsync(first);
        await queue.EnqueueAsync(second);
        await queue.EnqueueAsync(third);

        var d1 = await queue.DequeueAsync(CancellationToken.None);
        var d2 = await queue.DequeueAsync(CancellationToken.None);
        var d3 = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal(first.JobId, d1.JobId);
        Assert.Equal(second.JobId, d2.JobId);
        Assert.Equal(third.JobId, d3.JobId);
    }

    [Fact]
    public async Task Enqueue_RegistersJobAsQueued_InStatusStore()
    {
        var queue = CreateQueue(out var store);
        var job = new DocumentImportJobMessage(Guid.NewGuid());

        await queue.EnqueueAsync(job);

        var status = store.Get(job.JobId);
        Assert.NotNull(status);
        Assert.Equal(BackgroundJobState.Queued, status!.State);
        Assert.Equal("DocumentImport", status.JobType);
        Assert.Equal(job.TargetDocumentId, status.DocumentId);
    }

    [Fact]
    public async Task Dequeue_BlocksUntilItemAvailable()
    {
        var queue = CreateQueue(out _);
        var job = new ReembedJobMessage(null);

        // Start a dequeue before anything is enqueued; it must not complete yet.
        var dequeueTask = queue.DequeueAsync(CancellationToken.None).AsTask();
        Assert.False(dequeueTask.IsCompleted);

        await queue.EnqueueAsync(job);

        var dequeued = await dequeueTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(job.JobId, dequeued.JobId);
    }

    [Fact]
    public async Task Dequeue_Cancellation_ThrowsOperationCanceled()
    {
        var queue = CreateQueue(out _);
        using var cts = new CancellationTokenSource();

        var dequeueTask = queue.DequeueAsync(cts.Token).AsTask();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dequeueTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Enqueue_NullJob_Throws()
    {
        var queue = CreateQueue(out _);
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => queue.EnqueueAsync<DocumentImportJobMessage>(null!).AsTask());
    }
}
