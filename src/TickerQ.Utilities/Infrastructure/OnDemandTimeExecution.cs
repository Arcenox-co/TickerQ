using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

internal static class OnDemandTimeExecution
{
    internal static async Task<bool> RunAsync<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
        ITickerQDispatcher dispatcher, ITickerQNotificationHubSender notifier,
        string partition, Guid id, DateTime now, CancellationToken cancellationToken, ILogger logger = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (dispatcher == null || !dispatcher.IsEnabled)
            throw new InvalidOperationException("The scheduler must be running and licensed before a time ticker can run immediately.");
        var existing = await persistence.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false);
        if (existing == null || existing.ParentId.HasValue) return false;
        var acquired = await persistence.AcquireTimeTickerOnDemandAsync(id, now, cancellationToken).ConfigureAwait(false);
        if (acquired == null) return false;

        try
        {
            InternalFunctionContext[] contexts = [BuildContext(acquired, partition)];
            TickerExecutionContext.CacheFunctionReferences(contexts.AsSpan());
            await dispatcher.DispatchAsync(contexts, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogError(exception, "Time ticker {TickerId} was acquired, but immediate dispatch failed. Recovery will handle persisted work.", id);
        }
        try
        {
            if (notifier != null) await notifier.UpdateTimeTickerNotifyAsync(id).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogWarning(exception, "Time ticker {TickerId} was acquired, but its dashboard notification failed.", id);
        }
        return true;
    }

    private static InternalFunctionContext BuildContext(TimeTickerEntity ticker, string partition) => new()
    {
        RuntimePartitionKey = partition,
        FunctionName = ticker.Function,
        RequestContractVersion = ticker.RequestContractVersion,
        RequestContractFingerprint = ticker.RequestContractFingerprint,
        TickerId = ticker.Id,
        Type = TickerType.TimeTicker,
        Status = ticker.Status,
        Retries = ticker.Retries,
        RetryIntervals = ticker.RetryIntervals,
        TimeoutSeconds = ticker.TimeoutSeconds,
        ParentId = ticker.ParentId,
        AcquisitionToken = ticker.AcquisitionToken,
        ChainRootId = ticker.ChainRootId,
        ChainGeneration = ticker.ChainGeneration,
        ExecutionTime = ticker.ExecutionTime ?? DateTime.UtcNow,
        RunCondition = ticker.RunCondition ?? RunCondition.OnAnyCompletedStatus,
        TimeTickerChildren = ticker.Children?.Select(child => BuildContext(child, partition)).ToList() ?? []
    };
}
