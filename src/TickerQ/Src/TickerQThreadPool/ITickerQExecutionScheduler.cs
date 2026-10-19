using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.TickerQThreadPool;

/// <summary>Separates admitted job orchestration from the bounded user-handler execution budget.</summary>
internal interface ITickerQExecutionScheduler
{
    bool TryQueueOrchestration(Func<CancellationToken, Task> work, CancellationToken admissionToken = default);
    Task ExecuteAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken token = default);
    int ActiveOrchestrationCount { get; }
    IDisposable BeginFunctionWait();
    IDisposable BeginRetryDelay();
}
