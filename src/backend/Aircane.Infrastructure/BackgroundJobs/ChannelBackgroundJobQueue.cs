using System.Threading.Channels;
using Aircane.Application.Abstractions.BackgroundJobs;

namespace Aircane.Infrastructure.BackgroundJobs;

/// <summary>
/// In-process <see cref="IBackgroundJobQueue"/> backed by an unbounded
/// <see cref="Channel{T}"/>. Registered as a singleton so the enqueuing request scope and the
/// hosted worker share the same channel.
/// </summary>
/// <remarks>
/// Unbounded is acceptable for the local-first single-user profile: job volume is bounded by
/// a single operator's actions (uploads, scans, re-embeds). If a persistent runner is needed
/// later, swap this implementation behind <see cref="IBackgroundJobQueue"/>.
/// </remarks>
public sealed class ChannelBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<IBackgroundJob> _channel;
    private readonly IBackgroundJobStatusStore _statusStore;

    public ChannelBackgroundJobQueue(IBackgroundJobStatusStore statusStore)
    {
        _statusStore = statusStore;
        _channel = Channel.CreateUnbounded<IBackgroundJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync<T>(T job, CancellationToken ct = default)
        where T : IBackgroundJob
    {
        ArgumentNullException.ThrowIfNull(job);

        // Register the job as Queued before it is written so a status lookup immediately
        // after enqueue always resolves.
        _statusStore.Register(job);
        return _channel.Writer.WriteAsync(job, ct);
    }

    /// <inheritdoc />
    public async ValueTask<IBackgroundJob> DequeueAsync(CancellationToken ct)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}
