using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.RemoteExecutor.WorkerStream;

/// <summary>
/// Watches the worker-stream registry and toggles <c>CronTickerEntity.IsSystemPaused</c>
/// based on which SDK nodes are currently connected.
///
/// Why: cron tickers whose function targets a disconnected SDK would otherwise
/// keep firing into a black hole, accumulating "no live worker stream" failures
/// (or, with the new transport-retry behaviour, exhausting retries every poll).
/// Pausing them at the entity level removes them from polling entirely until
/// the SDK comes back, then auto-unpausing puts them right back in the
/// rotation. The user's manual <c>IsEnabled</c> flag is left alone — the two
/// semantics are deliberately separate so the dashboard can show "paused
/// (SDK offline)" without overwriting user intent.
///
/// Reconciliation runs:
///   * On every <see cref="WorkerStreamRegistry.Changed"/> event, debounced.
///   * Periodically (60s safety net) so a missed event doesn't strand crons.
///
/// Function names are stored qualified (<c>FunctionName@NodeName</c>). Crons
/// without an <c>@</c> are treated as scheduler-local — they're never paused
/// because they don't depend on an SDK.
/// </summary>
internal sealed class WorkerStreamPauseMonitor<TTimeTicker, TCronTicker> : BackgroundService
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PeriodicSweep = TimeSpan.FromSeconds(60);

    private readonly WorkerStreamRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkerStreamPauseMonitor<TTimeTicker, TCronTicker>>? _logger;
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    public WorkerStreamPauseMonitor(
        WorkerStreamRegistry registry,
        IServiceProvider services,
        ILogger<WorkerStreamPauseMonitor<TTimeTicker, TCronTicker>>? logger = null)
    {
        _registry = registry;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run an initial reconcile so the IsSystemPaused state matches reality
        // when the scheduler boots — without it, a cron paused before a
        // restart would stay paused forever even if the SDK is now connected.
        _ = ReconcileAsync(stoppingToken);

        _registry.Changed += OnRegistryChanged;
        try
        {
            // Periodic sweep is the safety net: if a Changed event ever
            // gets lost (process crash mid-handler, etc.) the next sweep
            // brings state back into sync within ~1 minute.
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(PeriodicSweep, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                _ = ReconcileAsync(stoppingToken);
            }
        }
        finally
        {
            _registry.Changed -= OnRegistryChanged;
        }
    }

    private void OnRegistryChanged()
    {
        // Coalesce bursts of register/unregister events (e.g. an SDK rolling
        // replicas) into a single reconcile pass after a short quiet period.
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceWindow, cts.Token).ConfigureAwait(false);
                await ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Newer event came in — the next debounce wins.
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Worker-stream pause reconcile failed");
            }
        });
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        // Single-flight: a periodic tick + a debounced event firing at the
        // same instant shouldn't both query + write concurrently.
        if (!await _reconcileLock.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            using var scope = _services.CreateScope();
            var persistence = scope.ServiceProvider
                .GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();

            // Snapshot of currently-connected node names (case-insensitive,
            // since RemoteFunctionRegistry stores them as the SDK declared
            // and PickForNode matches OrdinalIgnoreCase).
            var connectedNodes = _registry.All()
                .Select(c => c.NodeName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var cronQuery = persistence.CronTickersQuery();
            var crons = await cronQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var toUpdate = new List<TCronTicker>();
            foreach (var cron in crons)
            {
                var node = ExtractNode(cron.Function);
                if (string.IsNullOrEmpty(node))
                {
                    // Scheduler-local function (no `@node` suffix). Never
                    // touch its IsSystemPaused — local crons don't depend
                    // on any SDK. Force-clear if it somehow got set, so a
                    // misclassified row doesn't stay paused forever.
                    if (cron.IsSystemPaused)
                    {
                        cron.IsSystemPaused = false;
                        toUpdate.Add(cron);
                    }
                    continue;
                }

                var nodeOnline = connectedNodes.Contains(node);
                if (nodeOnline && cron.IsSystemPaused)
                {
                    cron.IsSystemPaused = false;
                    toUpdate.Add(cron);
                }
                else if (!nodeOnline && !cron.IsSystemPaused)
                {
                    cron.IsSystemPaused = true;
                    toUpdate.Add(cron);
                }
            }

            if (toUpdate.Count == 0) return;

            await persistence.UpdateCronTickers(toUpdate.ToArray(), cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(
                "Worker-stream pause reconcile updated {Count} cron tickers (connected nodes: {Nodes})",
                toUpdate.Count,
                string.Join(",", connectedNodes));
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Worker-stream pause reconcile threw");
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    /// <summary>
    /// Pulls the node name out of a qualified function string. Returns empty
    /// for bare names — those are scheduler-local and not subject to pausing.
    /// </summary>
    private static string ExtractNode(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return string.Empty;
        var idx = functionName.IndexOf('@');
        if (idx < 0 || idx == functionName.Length - 1) return string.Empty;
        return functionName[(idx + 1)..];
    }
}
