using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ;

/// <summary>
/// Bounds the store round-trips an execution makes outside its worker slot. A job's function runs
/// in one of <c>MaxConcurrency</c> worker slots, but its lifecycle and outcome writes — and a chain
/// step's read of its parent's result — run on the orchestration, and up to
/// <c>MaxQueuedExecutions</c> orchestrations can be live: a burst of due jobs or chains would
/// otherwise open one database connection per orchestration at once. A gated call never waits for
/// anything that itself needs the gate, so it cannot deadlock; a slow store slows completions
/// instead of exhausting its connections.
/// </summary>
internal sealed class ExecutionStoreGate
{
    private readonly IInternalTickerManager _manager;
    private readonly SemaphoreSlim _permits;

    internal ExecutionStoreGate(IInternalTickerManager manager, int maxConcurrentCalls)
    {
        _manager = manager;
        _permits = new SemaphoreSlim(Math.Max(1, maxConcurrentCalls));
    }

    internal Task UpdateTickerAsync(InternalFunctionContext context)
        => RunAsync(static (manager, context) => manager.UpdateTickerAsync(context, CancellationToken.None), context);

    internal Task UpdateTickerFromRemoteAsync(InternalFunctionContext context)
        => RunAsync(static (manager, context) => manager.UpdateTickerFromRemoteAsync(context, CancellationToken.None),
            context);

    internal Task UpdateTickerFromRemoteAsync(InternalFunctionContext context, NodeFinalizationIntent intent)
        => RunAsync(static (manager, state) => manager.UpdateTickerFromRemoteAsync(
            state.Context, state.Intent, CancellationToken.None), (Context: context, Intent: intent));

    internal Task UpdateSkipTimeTickersWithUnifiedContextAsync(InternalFunctionContext[] contexts)
        => RunAsync(static (manager, contexts) => manager.UpdateSkipTimeTickersWithUnifiedContextAsync(
            contexts, CancellationToken.None), contexts);

    internal Task ReleaseAcquiredResources(InternalFunctionContext[] contexts)
        => RunAsync(static (manager, contexts) => manager.ReleaseAcquiredResources(contexts, CancellationToken.None),
            contexts);

    internal Task ReleaseChainLeaseAsync(InternalFunctionContext root)
        => RunAsync(static (manager, root) => manager.ReleaseChainLeaseAsync(root, CancellationToken.None), root);

    // Only a time-ticker parent has a stored result; a cron occurrence's parent is its definition,
    // which the manager answers without the store, so it needs no permit.
    internal Task<TickerResultEnvelope> GetParentResultAsync(Guid parentId, TickerType parentType)
        => parentType != TickerType.TimeTicker
            ? _manager.GetParentResultAsync(parentId, parentType, CancellationToken.None)
            : RunAsync(static (manager, state) => manager.GetParentResultAsync(
                state.ParentId, state.ParentType, CancellationToken.None), (ParentId: parentId, ParentType: parentType));

    internal Task<bool> TryDispatchCronChainAsync(InternalFunctionContext context, bool isDue,
        CancellationToken cancellationToken)
        => RunAsync(static (manager, state) => manager.TryDispatchCronChainAsync(
            state.Context, state.IsDue, state.CancellationToken), (Context: context, IsDue: isDue, CancellationToken: cancellationToken));

    private async Task RunAsync<TState>(Func<IInternalTickerManager, TState, Task> call, TState state)
    {
        await _permits.WaitAsync().ConfigureAwait(false);
        try
        {
            await call(_manager, state).ConfigureAwait(false);
        }
        finally
        {
            _permits.Release();
        }
    }

    private async Task<TResult> RunAsync<TState, TResult>(Func<IInternalTickerManager, TState, Task<TResult>> call,
        TState state)
    {
        await _permits.WaitAsync().ConfigureAwait(false);
        try
        {
            return await call(_manager, state).ConfigureAwait(false);
        }
        finally
        {
            _permits.Release();
        }
    }
}
