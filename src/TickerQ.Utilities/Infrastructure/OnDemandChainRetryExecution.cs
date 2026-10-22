using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Infrastructure;

internal static class OnDemandChainRetryExecution
{
    internal static async Task<TTimeTicker> RunAsync<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
        ITickerQDispatcher dispatcher,
        ITickerQNotificationHubSender notifier,
        string partition,
        Guid selectedId,
        DateTime now,
        CancellationToken cancellationToken,
        ILogger logger = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (dispatcher == null || !dispatcher.IsEnabled)
            throw new InvalidOperationException(
                "The scheduler must be running and licensed before a chain step can be retried.");

        var acquisition = await persistence.AcquireTimeTickerChainRetryAsync(
            selectedId, now, cancellationToken).ConfigureAwait(false);
        if (acquisition == null) return null;

        var root = acquisition.Root;
        var context = TimeTickerChainRetryPlanner.BuildExecutionContext(acquisition, partition);

        try
        {
            TickerExecutionContext.CacheFunctionReferences([context]);
            await dispatcher.DispatchAsync([context], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogError(exception,
                "Chain {ChainRootId} was acquired for retry from {TickerId}, but dispatch failed.",
                root.Id, selectedId);
            try
            {
                await persistence.AbortTimeTickerChainRetryAsync(
                        root.Id, context.ChainGeneration!.Value,
                        "The chain retry could not be dispatched.", now, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception abortException)
            {
                (logger ?? NullLogger.Instance).LogError(abortException,
                    "Chain retry {ChainRootId} could not be moved to a safe terminal state after dispatch failed.",
                    root.Id);
            }
            throw new InvalidOperationException(
                "The chain retry could not be queued. No completed ancestor was run again.", exception);
        }

        try
        {
            if (notifier != null)
                await notifier.UpdateTimeTickerNotifyAsync(root.Id).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogWarning(exception,
                "Chain {ChainRootId} was acquired for retry, but its dashboard notification failed.", root.Id);
        }
        return root;
    }
}
