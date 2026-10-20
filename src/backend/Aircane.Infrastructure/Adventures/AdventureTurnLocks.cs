using System.Collections.Concurrent;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// One lock per adventure run, shared by everything that moves the turn or rewrites the turn order (passing the
/// turn, adding a participant, giving a seat to a new participant, removing a participant), so a check and the
/// change it leads to never interleave with another request's. Requests run in separate scopes and contexts, so
/// the locks are static; this app is a single process (the hub's connection registry is too).
/// <para>
/// Entries are never evicted: one small semaphore per run that has ever had its turn moved in this process.
/// Evicting safely would need reference counting (a waiter may hold the semaphore being removed), which is not
/// worth it at the number of runs one host plays.
/// </para>
/// </summary>
internal static class AdventureTurnLocks
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static SemaphoreSlim For(Guid runId) => Locks.GetOrAdd(runId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Takes the locks of all these runs, in a fixed order so two callers never deadlock.</summary>
    public static async Task<Held> AcquireAllAsync(IEnumerable<Guid> runIds, CancellationToken ct)
    {
        var held = new List<SemaphoreSlim>();
        try
        {
            foreach (var runId in runIds.Distinct().OrderBy(id => id))
            {
                if (TestHooks.TryGetValue(runId, out var hooks) && hooks.Bypass)
                    continue;
                var gate = For(runId);
                await gate.WaitAsync(ct);
                held.Add(gate);
            }
        }
        catch
        {
            new Held(held).Dispose();
            throw;
        }
        return new Held(held);
    }

    /// <summary>Takes one run's lock.</summary>
    public static Task<Held> AcquireAsync(Guid runId, CancellationToken ct) => AcquireAllAsync([runId], ct);

    /// <summary>The locks a caller holds; disposing releases them.</summary>
    public sealed class Held : IDisposable
    {
        private List<SemaphoreSlim>? _gates;

        internal Held(List<SemaphoreSlim> gates) => _gates = gates;

        public void Dispose()
        {
            var gates = Interlocked.Exchange(ref _gates, null);
            if (gates is null)
                return;
            for (var i = gates.Count - 1; i >= 0; i--)
                gates[i].Release();
        }
    }

    // ── Test seams ────────────────────────────────────────────────────────────
    // Keyed by run, so a test that pauses or bypasses one run's lock cannot affect any other run (tests run in
    // parallel). Nothing sets these outside tests.

    internal sealed class Hooks
    {
        /// <summary>Skip this run's lock, to show what goes wrong without it.</summary>
        public bool Bypass { get; init; }

        /// <summary>Called by each writer once it has read the run, before it writes: (writer, runId).</summary>
        public Func<string, Guid, Task>? Checkpoint { get; init; }
    }

    private static readonly ConcurrentDictionary<Guid, Hooks> TestHooks = new();

    internal static void SetTestHooks(Guid runId, Hooks hooks) => TestHooks[runId] = hooks;

    internal static void ClearTestHooks(Guid runId) => TestHooks.TryRemove(runId, out _);

    /// <summary>The point between a writer's read and its write, where a test can hold it.</summary>
    internal static Task CheckpointAsync(string writer, Guid runId)
        => TestHooks.TryGetValue(runId, out var hooks) && hooks.Checkpoint is { } checkpoint
            ? checkpoint(writer, runId)
            : Task.CompletedTask;
}
