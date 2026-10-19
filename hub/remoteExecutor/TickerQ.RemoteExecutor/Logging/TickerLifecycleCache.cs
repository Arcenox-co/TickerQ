using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor.Logging;

/// <summary>Bounded, expiring deduplication scoped to an execution generation.</summary>
internal sealed class TickerLifecycleCache
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, State> _states = new();
    private readonly LinkedList<Guid> _oldest = new();
    private readonly int _capacity;
    private readonly TimeSpan _retention;
    private readonly Func<DateTime> _utcNow;

    public TickerLifecycleCache(int capacity = 4096, TimeSpan? retention = null, Func<DateTime>? utcNow = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _retention = retention ?? TimeSpan.FromMinutes(30);
        if (_retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    internal int Count { get { lock (_gate) { SweepCore(_utcNow()); return _states.Count; } } }
    internal void Sweep() { lock (_gate) SweepCore(_utcNow()); }

    public bool TryTransition(InternalFunctionContext context, out TickerStatus? previous)
    {
        lock (_gate)
        {
            var now = _utcNow();
            SweepCore(now);
            previous = null;
            if (_states.TryGetValue(context.TickerId, out var state))
            {
                var newGeneration = context.AcquisitionToken.HasValue && state.Generation.HasValue &&
                    context.AcquisitionToken != state.Generation;
                var restarted = state.Terminal && !IsTerminal(context.Status);
                var newerRun = state.Terminal && context.ExecutedAt != default &&
                    state.ExecutedAt != default && context.ExecutedAt > state.ExecutedAt;
                if (newGeneration || restarted || newerRun)
                {
                    state.Terminal = false;
                    state.Status = null;
                }
                _oldest.Remove(state.Node);
                _oldest.AddLast(state.Node);
            }
            else
            {
                if (_states.Count == _capacity) RemoveOldest();
                state = new State(_oldest.AddLast(context.TickerId));
                _states.Add(context.TickerId, state);
            }
            previous = state.Status;
            state.LastTouched = now;
            state.Generation = context.AcquisitionToken;
            state.ExecutedAt = context.ExecutedAt;
            if (previous == context.Status) return false;
            state.Status = context.Status;
            if (!IsTerminal(context.Status)) return true;
            if (state.Terminal) return false;
            state.Terminal = true;
            return true;
        }
    }

    private void SweepCore(DateTime now)
    {
        while (_oldest.First is { } first && now - _states[first.Value].LastTouched >= _retention)
            RemoveOldest();
    }

    private void RemoveOldest()
    {
        _states.Remove(_oldest.First!.Value);
        _oldest.RemoveFirst();
    }

    private static bool IsTerminal(TickerStatus status) => status is TickerStatus.Done or TickerStatus.DueDone
        or TickerStatus.Failed or TickerStatus.Cancelled;

    private sealed class State(LinkedListNode<Guid> node)
    {
        public readonly LinkedListNode<Guid> Node = node;
        public DateTime LastTouched;
        public DateTime ExecutedAt;
        public Guid? Generation;
        public TickerStatus? Status;
        public bool Terminal;
    }
}
