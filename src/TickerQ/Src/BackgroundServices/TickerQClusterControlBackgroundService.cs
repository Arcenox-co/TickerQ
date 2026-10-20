using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

internal sealed class TickerQClusterControlBackgroundService<TTimeTicker, TCronTicker>(
    ITickerPersistenceProvider<TTimeTicker, TCronTicker> store,
    ITickerQTaskScheduler taskScheduler,
    ITickerQHostScheduler hostScheduler,
    SchedulerOptionsBuilder options,
    ITickerClock clock,
    ILogger<TickerQClusterControlBackgroundService<TTimeTicker, TCronTicker>> logger) : BackgroundService
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NodeRetention = TimeSpan.FromDays(1);
    private static readonly TimeSpan CommandRetention = TimeSpan.FromDays(1);
    private readonly DateTime _startedAtUtc = clock.UtcNow;
    private readonly string _nodeKey = TickerQClusterIdentity.NodeKey(options.ExecutionOwnerId);
    private DateTime _nextHeartbeatUtc;
    private DateTime _nextPruneUtc;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (store.SupportsClusterControl)
            await WriteHeartbeatAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!store.SupportsClusterControl) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.UtcNow;
                if (now >= _nextHeartbeatUtc) await WriteHeartbeatAsync(stoppingToken).ConfigureAwait(false);
                await ProcessCommandsAsync(now, stoppingToken).ConfigureAwait(false);
                if (now >= _nextPruneUtc)
                {
                    await store.PruneClusterControlAsync(now - NodeRetention, now - CommandRetention, stoppingToken)
                        .ConfigureAwait(false);
                    _nextPruneUtc = now.AddMinutes(10);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "TickerQ cluster-control polling failed; it will retry.");
            }
            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!store.SupportsClusterControl) return;
        try { await store.UpsertNodeAsync(CreateNode(clock.UtcNow, false), cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { logger.LogDebug(exception, "TickerQ final node heartbeat could not be saved."); }
    }

    internal async Task ProcessCommandsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var commands = await store.GetPendingCommandsAsync(options.ExecutionOwnerId, 50, cancellationToken)
            .ConfigureAwait(false);
        foreach (var command in commands)
        {
            var status = TickerQControlCommandStatus.Completed;
            string outcome;
            if (command.ExpiresAtUtc <= now)
            {
                status = TickerQControlCommandStatus.Expired;
                outcome = "The cancellation request expired before the owning node processed it.";
            }
            else
            {
                var signalled = TickerCancellationTokenManager.RequestTickerCancellation(
                    (options.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey,
                    new TickerExecutionLease(command.TickerType, command.TickerId, command.AcquisitionToken));
                if (!signalled)
                {
                    // Acquisition persists ownership immediately before publishing into the local
                    // execution queue. Keep the command pending across that small handoff window.
                    var owner = await store.GetExecutionOwnershipAsync(
                        command.TickerType, command.TickerId, cancellationToken).ConfigureAwait(false);
                    if (owner != null
                        && owner.Status is TickerStatus.Queued or TickerStatus.InProgress
                        && StringComparer.Ordinal.Equals(owner.ExecutionOwnerId, command.TargetExecutionOwnerId)
                        && owner.AcquisitionToken == command.AcquisitionToken)
                        continue;
                }
                outcome = signalled
                    ? "Cancellation was signalled to the running execution."
                    : "The execution generation is no longer running on this node.";
            }
            await store.CompleteCommandAsync(command.Id, status, outcome, now, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task WriteHeartbeatAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        _nextHeartbeatUtc = now + HeartbeatInterval;
        return store.UpsertNodeAsync(CreateNode(now, hostScheduler.IsRunning), cancellationToken);
    }

    private TickerQNodeRecord CreateNode(DateTime now, bool running) => new()
    {
        NodeKey = _nodeKey,
        ExecutionOwnerId = options.ExecutionOwnerId,
        NodeIdentifier = options.NodeIdentifier,
        DisplayName = string.IsNullOrWhiteSpace(options.NodeDisplayName) ? options.NodeIdentifier : options.NodeDisplayName,
        DashboardUrl = options.AdvertisedDashboardUrl,
        Version = typeof(TickerQClusterControlBackgroundService<,>).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(TickerQClusterControlBackgroundService<,>).Assembly.GetName().Version?.ToString() ?? "",
        StartedAtUtc = _startedAtUtc,
        LastSeenAtUtc = now,
        IsRunning = running,
        MaxConcurrency = options.MaxConcurrency,
        FunctionCount = TickerFunctionProvider.Snapshot.Descriptors.Count,
        ActiveExecutions = taskScheduler.ActiveExecutionCount,
        QueuedExecutions = taskScheduler.TotalQueuedTasks,
        ActiveWorkflows = taskScheduler.ActiveOrchestrationCount,
        WaitingForFunction = taskScheduler.WaitingForFunctionCount,
        RetryingExecutions = taskScheduler.RetryingCount
    };
}
