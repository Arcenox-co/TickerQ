namespace TickerQ.RemoteExecutor.Execution;

internal sealed class NodeCircuitBreaker
{
    private enum State { Closed, Open, HalfOpen }

    private int _state = (int)State.Closed;
    private int _consecutiveFailures;
    private long _openedAtTicks;

    private readonly int _failureThreshold;
    private readonly long _cooldownTicks;

    public NodeCircuitBreaker(int failureThreshold, TimeSpan cooldown)
    {
        _failureThreshold = failureThreshold > 0 ? failureThreshold : 5;
        _cooldownTicks = cooldown.Ticks > 0 ? cooldown.Ticks : TimeSpan.FromSeconds(30).Ticks;
    }

    public bool AllowRequest()
    {
        var state = (State)Volatile.Read(ref _state);

        switch (state)
        {
            case State.Closed:
                return true;

            case State.Open:
                if (DateTime.UtcNow.Ticks - Volatile.Read(ref _openedAtTicks) >= _cooldownTicks)
                {
                    // Cooldown elapsed — transition to HalfOpen (one probe allowed)
                    Interlocked.CompareExchange(ref _state, (int)State.HalfOpen, (int)State.Open);
                    return true;
                }
                return false;

            case State.HalfOpen:
                return true;

            default:
                return true;
        }
    }

    public void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _state, (int)State.Closed);
    }

    public void RecordFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);

        if (failures >= _failureThreshold)
        {
            Interlocked.Exchange(ref _openedAtTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _state, (int)State.Open);
        }
    }

    public bool IsOpen => (State)Volatile.Read(ref _state) == State.Open;
}
