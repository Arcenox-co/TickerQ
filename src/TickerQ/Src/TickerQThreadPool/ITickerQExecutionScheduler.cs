using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.TickerQThreadPool;

/// <summary>Separates admitted job orchestration from the bounded user-handler execution budget.</summary>
internal interface ITickerQExecutionScheduler
{
    bool TryQueueOrchestration(Func<CancellationToken, Task> work, CancellationToken admissionToken = default);
    Task WaitForOrchestrationCapacityAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Runs <paramref name="work"/> in an execution slot. The token handed to it is cancelled
    /// whenever <paramref name="token"/> is, so callers need not link the two again.
    /// </summary>
    Task ExecuteAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken token = default);
    /// <summary>
    /// <see cref="ExecuteAsync"/> for a caller whose <paramref name="token"/> is already cancelled whenever
    /// <paramref name="stopToken"/> is. When <paramref name="stopToken"/> is this scheduler's own stop token,
    /// the scheduler need not link the work's token to its stop again. Any other value behaves as
    /// <see cref="ExecuteAsync"/>.
    /// </summary>
    Task ExecuteStopBoundAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority,
        CancellationToken token, CancellationToken stopToken)
        => ExecuteAsync(work, priority, token);
    int ActiveOrchestrationCount { get; }
    /// <summary>True from the moment the node stops admitting work until it is resumed.</summary>
    bool IsFrozen { get; }
    IDisposable BeginFunctionWait();
    IDisposable BeginRetryDelay();
}
