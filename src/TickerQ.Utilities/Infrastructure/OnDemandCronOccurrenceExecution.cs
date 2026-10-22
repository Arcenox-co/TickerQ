using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>Re-runs one standalone cron occurrence without inserting another occurrence row.</summary>
internal static class OnDemandCronOccurrenceExecution
{
    internal static async Task<bool> RunAsync<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
        ITickerQDispatcher dispatcher,
        ITickerQNotificationHubSender notifier,
        string runtimePartitionKey,
        Guid occurrenceId,
        DateTime utcNow,
        CancellationToken cancellationToken,
        ILogger logger = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (dispatcher == null || !dispatcher.IsEnabled)
            throw new InvalidOperationException(
                "The scheduler must be running and licensed before a cron occurrence can be re-run.");

        var acquired = await persistence.AcquireCronOccurrenceOnDemandAsync(
            occurrenceId, utcNow, cancellationToken).ConfigureAwait(false);
        if (acquired?.CronTicker == null) return false;

        var cron = acquired.CronTicker;
        var context = new InternalFunctionContext
        {
            RuntimePartitionKey = runtimePartitionKey,
            TickerId = acquired.Id,
            ParentId = acquired.CronTickerId,
            Type = TickerType.CronTickerOccurrence,
            Status = acquired.Status,
            AcquisitionToken = acquired.AcquisitionToken,
            ExecutionTime = acquired.ExecutionTime,
            FunctionName = cron.Function,
            RequestContractVersion = cron.RequestContractVersion,
            RequestContractFingerprint = cron.RequestContractFingerprint,
            Retries = cron.Retries,
            RetryIntervals = cron.RetryIntervals,
            SkipIfAlreadyRunning = cron.SkipIfAlreadyRunning,
            TimeoutSeconds = cron.TimeoutSeconds,
        };
        InternalFunctionContext[] contexts = [context];
        TickerExecutionContext.CacheFunctionReferences(contexts.AsSpan());

        try
        {
            await dispatcher.DispatchAsync(contexts, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogError(exception,
                "Cron occurrence {OccurrenceId} was acquired for re-run, but dispatch failed. Recovery will handle persisted work.",
                occurrenceId);
        }

        try
        {
            if (notifier != null)
                await notifier.UpdateCronOccurrenceAsync(acquired.CronTickerId, occurrenceId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogWarning(exception,
                "Cron occurrence {OccurrenceId} was acquired for re-run, but its dashboard notification failed.",
                occurrenceId);
        }
        return true;
    }
}
