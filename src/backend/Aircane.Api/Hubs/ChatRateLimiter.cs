using System.Collections.Concurrent;

namespace Aircane.Api.Hubs;

/// <summary>
/// A sliding-window limit on chat sends per sender: at most <see cref="MaxMessages"/> in
/// <see cref="Window"/>. The hub has no HTTP request to put the usual rate limiter in front of, and chat is
/// stored, so a client looping on SendChatMessage would otherwise fill the history and flood the table.
/// One process, so in memory.
/// </summary>
public sealed class ChatRateLimiter
{
    public const int MaxMessages = 10;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<(Guid Session, Guid Sender), Queue<DateTimeOffset>> _sends = new();
    private readonly Func<DateTimeOffset> _now;

    public ChatRateLimiter() : this(() => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>For tests: a clock to control.</summary>
    public ChatRateLimiter(Func<DateTimeOffset> now) => _now = now;

    /// <summary>True (and counted) when the sender may send now; false when they are over the limit.</summary>
    public bool TryAcquire(Guid sessionId, Guid senderId)
    {
        var now = _now();
        var queue = _sends.GetOrAdd((sessionId, senderId), _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= Window)
                queue.Dequeue();

            if (queue.Count >= MaxMessages)
                return false;

            queue.Enqueue(now);
            return true;
        }
    }
}
