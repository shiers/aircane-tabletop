using Aircane.Application.Abstractions.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aircane.Workers.BackgroundJobs;

/// <summary>
/// Hosted service that continuously dequeues background jobs from the
/// <see cref="IBackgroundJobQueue"/> and dispatches each to its registered
/// <see cref="IJobHandler{T}"/> inside a fresh DI scope.
/// </summary>
/// <remarks>
/// One job is processed at a time. This keeps DbContext usage safe (a scoped DbContext is
/// resolved per job) and matches the local-first profile where job volume is low. Handler
/// exceptions are caught, logged, and recorded on the status store so a single failure never
/// stops the worker loop.
/// </remarks>
public sealed class BackgroundJobWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundJobStatusStore _statusStore;
    private readonly ILogger<BackgroundJobWorker> _logger;

    public BackgroundJobWorker(
        IBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IBackgroundJobStatusStore statusStore,
        ILogger<BackgroundJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _statusStore = statusStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackgroundJobWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            IBackgroundJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessJobAsync(job, stoppingToken);
        }

        _logger.LogInformation("BackgroundJobWorker stopping.");
    }

    private async Task ProcessJobAsync(IBackgroundJob job, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing background job {JobId} ({JobType}).", job.JobId, job.JobType);

        _statusStore.MarkRunning(job.JobId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await DispatchAsync(scope.ServiceProvider, job, ct);

            _statusStore.MarkCompleted(job.JobId);
            _logger.LogInformation(
                "Background job {JobId} ({JobType}) completed.", job.JobId, job.JobType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown in progress; leave the job in Running so it isn't mis-reported as failed.
            _logger.LogWarning(
                "Background job {JobId} ({JobType}) cancelled during shutdown.",
                job.JobId, job.JobType);
        }
        catch (Exception ex)
        {
            _statusStore.MarkFailed(job.JobId, ex.Message);
            _logger.LogError(ex,
                "Background job {JobId} ({JobType}) failed.", job.JobId, job.JobType);
        }
    }

    /// <summary>
    /// Resolves the correct <see cref="IJobHandler{T}"/> for the concrete job type and invokes it.
    /// Uses the runtime type so a single worker can dispatch every registered job type.
    /// </summary>
    private static async Task DispatchAsync(
        IServiceProvider services,
        IBackgroundJob job,
        CancellationToken ct)
    {
        var handlerType = typeof(IJobHandler<>).MakeGenericType(job.GetType());
        var handler = services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No {handlerType} registered for job type '{job.JobType}'.");

        var method = handlerType.GetMethod(nameof(IJobHandler<IBackgroundJob>.HandleAsync))!;

        Task task;
        try
        {
            task = (Task)method.Invoke(handler, [job, ct])!;
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Unwrap so the caller sees the real handler exception, not the reflection wrapper.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // unreachable
        }

        await task;
    }
}
