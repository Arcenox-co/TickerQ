namespace TickerQ.Tests;

/// <summary>Waits for test work without blocking the thread pool that executes it.</summary>
internal sealed class AsyncTestCountdown(int count)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _remaining = count;

    internal int CurrentCount => Volatile.Read(ref _remaining);

    internal void Signal()
    {
        var remaining = Interlocked.Decrement(ref _remaining);
        if (remaining < 0)
            throw new InvalidOperationException("The test countdown was signalled too many times.");
        if (remaining == 0)
            _completion.TrySetResult();
    }

    internal async Task<bool> WaitAsync(TimeSpan timeout)
    {
        try
        {
            await _completion.Task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
