using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

internal sealed class TickerQExecutionLogBackgroundService(InMemoryTickerExecutionLogStore memory,
    ExecutionLogOptions options, SchedulerOptionsBuilder scheduler, IServiceProvider services,
    ITickerQActivationGate activation, ILogger<TickerQExecutionLogBackgroundService> logger,
    TickerQRuntimeDiagnostics diagnostics = null) : BackgroundService
{
    private bool _ready;
    private DateTime _nextCleanup;
    private DateTime _nextWarning;
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.PersistToDatabase && services.GetService<ITickerExecutionLogPersistence>() == null)
            throw new InvalidOperationException("Database execution logs require an EF Core, MongoDB or Redis operational store (or ITickerExecutionLogPersistence).");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (scheduler.RuntimeSchedulerEnabled) await activation.WaitForActivationAsync(stoppingToken);
        _ready = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            await FlushSafelyAsync(stoppingToken);
            try { await Task.Delay(options.PersistToDatabase ? options.FlushInterval : options.CleanupInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        memory.Prune();
        if (!options.PersistToDatabase) return;
        var storage = services.GetRequiredService<ITickerExecutionLogPersistence>();
        var partition = (scheduler.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;
        var pending = memory.Pending(options.WriteBatchSize);
        if (pending.Length > 0)
        {
            try
            {
                await storage.WriteAsync(pending.Select(x => ExecutionLogCodec.Encode(x, partition, options.Retention)).ToArray(), cancellationToken);
                diagnostics?.FlushSucceeded();
            }
            catch (Exception)
            {
                diagnostics?.FlushFailed();
                throw;
            }
            foreach (var item in pending) memory.MarkPersisted(item.TickerId, item.Version);
        }
        if (pending.Length > 0 || DateTime.UtcNow >= _nextCleanup)
        {
            await storage.PruneAsync(partition, DateTime.UtcNow.Ticks, options.MaxStoredExecutions, Math.Max(options.WriteBatchSize, 100), cancellationToken);
            _nextCleanup = DateTime.UtcNow + options.CleanupInterval;
        }
    }

    private async Task FlushSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.StorageTimeout);
            await FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (DateTime.UtcNow >= _nextWarning)
            {
                _nextWarning = DateTime.UtcNow.AddMinutes(1);
                logger.LogWarning("TickerQ diagnostic log persistence is unavailable ({ErrorType}); bounded buffers will retry. Check connectivity and the execution-log schema.", ex.GetType().Name);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_ready && options.PersistToDatabase)
            for (var i = 0; i < (options.MaxBufferedExecutions + options.WriteBatchSize - 1) / options.WriteBatchSize
                 && !cancellationToken.IsCancellationRequested && memory.Pending(1).Length > 0; i++)
                await FlushSafelyAsync(cancellationToken);
    }
}
