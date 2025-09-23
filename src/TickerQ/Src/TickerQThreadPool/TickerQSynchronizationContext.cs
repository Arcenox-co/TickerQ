using System.Threading;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Custom SynchronizationContext that keeps async continuations on TickerQ worker threads.
/// Ensures true concurrency control by preventing execution from switching to ThreadPool.
/// Uses flexible thread assignment for optimal performance and load balancing.
/// </summary>
internal sealed class TickerQSynchronizationContext : SynchronizationContext
{
    private readonly TickerQTaskScheduler _scheduler;

    public TickerQSynchronizationContext(TickerQTaskScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Posts async continuations to TickerQ workers with circular dependency protection.
    /// Uses direct execution if called from within a TickerQ worker to prevent deadlocks.
    /// </summary>
    public override void Post(SendOrPostCallback d, object state)
    {
        // Check if we're already on a TickerQ worker thread using ThreadStatic flag
        if (TickerQTaskScheduler.IsTickerQWorkerThread)
        {
            // We're on a TickerQ worker - execute directly to avoid circular queueing
            try 
            { 
                d(state); 
            } 
            catch 
            { 
                /* swallow continuation exceptions */ 
            }
        }
        else
        {
            // We're not on a TickerQ worker - safe to queue the continuation
            _scheduler.QueueContinuation(() => d(state));
        }
    }

    /// <summary>
    /// Sends synchronous operations (not typically used in async scenarios).
    /// </summary>
    public override void Send(SendOrPostCallback d, object state)
    {
        // For synchronous operations, execute directly
        d(state);
    }
}