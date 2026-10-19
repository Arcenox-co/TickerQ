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

/// <summary>Shared manual-cron path for single runs and dashboard bulk retries.</summary>
internal static class OnDemandCronExecution
{
    internal static async Task<CronTickerOccurrenceEntity<TCronTicker>> RunAsync<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
        ITickerQDispatcher dispatcher,
        ITickerQNotificationHubSender notifier,
        string runtimePartitionKey,
        Guid cronTickerId,
        CancellationToken cancellationToken,
        ILogger logger = null,
        DateTime? utcNow = null,
        TickerLifecycleLogger lifecycle = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (dispatcher == null || !dispatcher.IsEnabled)
            throw new InvalidOperationException("The scheduler must be running and licensed before a cron can run immediately.");

        var cron = await persistence.GetCronTickerById(cronTickerId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cron ticker {cronTickerId} not found");
        var now = utcNow ?? DateTime.UtcNow;
        var occurrence = new CronTickerOccurrenceEntity<TCronTicker>
        {
            Id = Guid.NewGuid(),
            ApplicationNamespaceKey = runtimePartitionKey,
            CronTickerId = cronTickerId,
            DefinitionRevision = cron.DefinitionRevision,
            Status = TickerStatus.Idle,
            ExecutionTime = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        var inserted = await persistence.InsertCronTickerOccurrences([occurrence], cancellationToken).ConfigureAwait(false);
        if (inserted <= 0)
            throw new InvalidOperationException("The manual cron occurrence could not be created.");

        lifecycle?.Created(occurrence.Id, cron.Function, TickerType.CronTickerOccurrence, now, runtimePartitionKey);

        // After committing the occurrence, admission and execution must outlive the
        // HTTP request. Acquire atomically so a scheduler/fallback race cannot run it twice.
        try
        {
            var acquired = await persistence.AcquireImmediateCronOccurrencesAsync(
                [occurrence.Id], CancellationToken.None).ConfigureAwait(false);
            if (acquired.Length > 0)
            {
                var winner = acquired[0];
                var definition = winner.CronTicker ?? cron;
                var context = new InternalFunctionContext
                {
                    RuntimePartitionKey = runtimePartitionKey,
                    TickerId = winner.Id,
                    ParentId = winner.CronTickerId,
                    Type = TickerType.CronTickerOccurrence,
                    Status = winner.Status,
                    AcquisitionToken = winner.AcquisitionToken,
                    ExecutionTime = winner.ExecutionTime,
                    FunctionName = definition.Function,
                    RequestContractVersion = definition.RequestContractVersion,
                    RequestContractFingerprint = definition.RequestContractFingerprint,
                    Retries = definition.Retries,
                    RetryIntervals = definition.RetryIntervals,
                    SkipIfAlreadyRunning = definition.SkipIfAlreadyRunning,
                    TimeoutSeconds = definition.TimeoutSeconds
                };
                InternalFunctionContext[] contexts = [context];
                TickerExecutionContext.CacheFunctionReferences(contexts.AsSpan());
                // Direct dispatch uses isDue=false. Overdue recovery alone uses DueDone.
                await dispatcher.DispatchAsync(contexts, CancellationToken.None).ConfigureAwait(false);
            }

        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogError(exception,
                "Manual cron occurrence {OccurrenceId} was saved, but immediate dispatch failed. Recovery will handle persisted work.", occurrence.Id);
        }
        try
        {
            if (notifier != null)
                await notifier.AddCronOccurrenceAsync(cronTickerId, occurrence.Id).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger.Instance).LogWarning(exception, "Manual cron occurrence {OccurrenceId} was saved, but its dashboard notification failed.", occurrence.Id);
        }
        return occurrence;
    }
}
