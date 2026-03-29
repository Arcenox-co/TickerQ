namespace TickerQ.RemoteExecutor.Execution;

internal sealed class NodeState
{
    private int _dispatched;
    private int _completed;
    private int _maxConcurrency;
    private int _isDraining;

    public NodeState(int maxConcurrency)
    {
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : int.MaxValue;
    }

    public int ActiveTasks => Math.Max(0, Volatile.Read(ref _dispatched) - Volatile.Read(ref _completed));

    public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

    public int AvailableSlots => Math.Max(0, MaxConcurrency - ActiveTasks);

    public bool IsDraining => Volatile.Read(ref _isDraining) == 1;

    public bool CanAcceptWork => !IsDraining && AvailableSlots > 0;

    public void RecordDispatched() => Interlocked.Increment(ref _dispatched);

    public void RecordCompleted() => Interlocked.Increment(ref _completed);

    public void UpdateCapacity(int activeTasks, int maxConcurrency)
    {
        // SDK reported its actual state — reset our counters to match
        Interlocked.Exchange(ref _dispatched, activeTasks);
        Interlocked.Exchange(ref _completed, 0);
        if (maxConcurrency > 0)
            Interlocked.Exchange(ref _maxConcurrency, maxConcurrency);
    }

    public void SetDraining(bool draining) =>
        Interlocked.Exchange(ref _isDraining, draining ? 1 : 0);
}
