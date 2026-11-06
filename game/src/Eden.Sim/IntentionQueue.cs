using System.Collections.Concurrent;
using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Lock-free multi-producer queue of intentions, drained by the sim thread at the start of each batch.
/// <see cref="TryPush"/> never blocks: it refuses when the queue is full or the latest snapshot shows the
/// NPC has died. Ids the sim never saw are accepted here and rejected by <see cref="IntentionValidator"/>.
/// </summary>
public sealed class IntentionQueue(int capacity, Func<WorldSnapshot?> latest) : IIntentionSink
{
    private readonly ConcurrentQueue<Intention> _queue = new();
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public bool TryPush(Intention intention)
    {
        ArgumentNullException.ThrowIfNull(intention);
        if (latest() is { } snap && snap.IsKnownDead(intention.NpcId)) return false;
        if (Interlocked.Increment(ref _count) > capacity)
        {
            Interlocked.Decrement(ref _count);
            return false;
        }
        _queue.Enqueue(intention);
        return true;
    }

    public bool TryDequeue(out Intention intention)
    {
        if (!_queue.TryDequeue(out intention!)) return false;
        Interlocked.Decrement(ref _count);
        return true;
    }
}
