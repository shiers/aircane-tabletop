using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Infrastructure.BackgroundJobs;
using Aircane.Workers.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.BackgroundJobs;

/// <summary>
/// Unit tests for <see cref="BackgroundJobWorker"/>: it dequeues a job, dispatches it to the
/// matching <see cref="IJobHandler{T}"/> in a scope, and records terminal status.
/// </summary>
public class BackgroundJobWorkerTests
{
    private sealed class RecordingReembedHandler : IJobHandler<ReembedJobMessage>
    {
        public List<Guid> Handled { get; } = new();

        public Task HandleAsync(ReembedJobMessage job, CancellationToken ct)
        {
            Handled.Add(job.JobId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingReembedHandler : IJobHandler<ReembedJobMessage>
    {
        public Task HandleAsync(ReembedJobMessage job, CancellationToken ct)
            => throw new InvalidOperationException("handler failed");
    }

    private static (BackgroundJobWorker worker, ChannelBackgroundJobQueue queue, InMemoryBackgroundJobStatusStore store)
        Build(Action<IServiceCollection> configureHandlers)
    {
        var store = new InMemoryBackgroundJobStatusStore();
        var queue = new ChannelBackgroundJobQueue(store);

        var services = new ServiceCollection();
        configureHandlers(services);
        var provider = services.BuildServiceProvider();

        var worker = new BackgroundJobWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            store,
            NullLogger<BackgroundJobWorker>.Instance);

        return (worker, queue, store);
    }

    [Fact]
    public async Task Worker_DispatchesJob_ToMatchingHandler_AndMarksCompleted()
    {
        var handler = new RecordingReembedHandler();
        var (worker, queue, store) = Build(s => s.AddScoped<IJobHandler<ReembedJobMessage>>(_ => handler));

        var job = new ReembedJobMessage(null);
        await queue.EnqueueAsync(job);

        using var cts = new CancellationTokenSource();
        var runTask = worker.StartAsync(cts.Token);

        // Wait until the handler runs and the status flips to Completed.
        await WaitForStateAsync(store, job.JobId, BackgroundJobState.Completed);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(job.JobId, handler.Handled);
        Assert.Equal(BackgroundJobState.Completed, store.Get(job.JobId)!.State);
    }

    [Fact]
    public async Task Worker_HandlerThrows_MarksFailed_AndKeepsRunning()
    {
        var (worker, queue, store) = Build(s => s.AddScoped<IJobHandler<ReembedJobMessage>, ThrowingReembedHandler>());

        var failing = new ReembedJobMessage(null);
        await queue.EnqueueAsync(failing);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await WaitForStateAsync(store, failing.JobId, BackgroundJobState.Failed);

        var status = store.Get(failing.JobId)!;
        Assert.Equal(BackgroundJobState.Failed, status.State);
        Assert.Equal("handler failed", status.ErrorMessage);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForStateAsync(
        IBackgroundJobStatusStore store,
        Guid jobId,
        BackgroundJobState expected,
        int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (store.Get(jobId)?.State == expected)
                return;
            await Task.Delay(20);
        }

        Assert.Fail($"Job {jobId} did not reach state {expected} within {timeoutMs}ms " +
            $"(current: {store.Get(jobId)?.State.ToString() ?? "unknown"}).");
    }
}
