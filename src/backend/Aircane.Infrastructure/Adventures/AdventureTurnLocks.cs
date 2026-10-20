using System.Collections.Concurrent;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// One lock per adventure run, shared by everything that moves the turn or rewrites the turn order (passing the
/// turn, giving a seat to a new participant), so a check and the change it leads to never interleave with
/// another request's. Requests run in separate scopes and contexts, so the locks are static; this app is a single
/// process (the hub's connection registry is too).
/// </summary>
internal static class AdventureTurnLocks
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static SemaphoreSlim For(Guid runId) => Locks.GetOrAdd(runId, _ => new SemaphoreSlim(1, 1));
}
