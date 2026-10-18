using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

namespace TickerQ.BackgroundServices;

internal class TickerQFallbackBackgroundService :  BackgroundService
{
    private int _started;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly AcquiredExecutionQueue _acquiredQueue;
    private readonly bool _ownsAcquiredQueue;
    private readonly ITickerQTaskScheduler _tickerQTaskScheduler;
    private readonly TickerQLicenseStateProvider _licenseState;
    private readonly TimeSpan _fallbackJobPeriod;
    private readonly ITickerQActivationGate _activationGate;
    private readonly ILogger<TickerQFallbackBackgroundService> _logger;
    private readonly TickerQRuntimeDiagnostics _diagnostics;
    private DateTime _nextWarning;

    public TickerQFallbackBackgroundService(IInternalTickerManager internalTickerManager, SchedulerOptionsBuilder schedulerOptions, ITickerExecutionTaskHandler tickerExecutionTaskHandler, ITickerQTaskScheduler tickerQTaskScheduler, TickerQLicenseStateProvider licenseState, ITickerQActivationGate activationGate = null,
        ILogger<TickerQFallbackBackgroundService> logger = null, TickerQRuntimeDiagnostics diagnostics = null, AcquiredExecutionQueue acquiredQueue = null)
    {
        _internalTickerManager = internalTickerManager;
        _fallbackJobPeriod = schedulerOptions.FallbackIntervalChecker;
        _tickerQTaskScheduler = tickerQTaskScheduler;
        _licenseState = licenseState;
        _activationGate = activationGate;
        _logger = logger ?? NullLogger<TickerQFallbackBackgroundService>.Instance;
        _diagnostics = diagnostics;
        _ownsAcquiredQueue = acquiredQueue == null;
        _acquiredQueue = acquiredQueue ?? new AcquiredExecutionQueue(internalTickerManager,
            tickerExecutionTaskHandler, tickerQTaskScheduler, _logger, licenseState);
    }

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 
            ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_licenseState.ExecutionAllowed)
            return;

        if (_activationGate != null)
            await _activationGate.WaitForActivationAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested && _licenseState.ExecutionAllowed)
        {
            try
            {
                // If the scheduler is frozen or disposed (e.g., manual start mode or shutdown),
                // skip queuing fallback work to avoid throwing and stopping the host.
                if (_tickerQTaskScheduler.IsFrozen || _tickerQTaskScheduler.IsDisposed)
                {
                    await Task.Delay(_fallbackJobPeriod, stoppingToken);
                    continue;
                }

                var functions = await _internalTickerManager.RunTimedOutTickers(stoppingToken);

                if (functions.Length != 0)
                {
                    TickerExecutionContext.CacheFunctionReferences(functions.AsSpan());
                    if (await _acquiredQueue.PublishAsync(functions, true, stoppingToken).ConfigureAwait(false))
                        _diagnostics?.FallbackSucceeded();
                    else
                        _diagnostics?.FallbackFailed();
                    await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
                }
                else
                {
                    _diagnostics?.FallbackSucceeded();
                    await Task.Delay(_fallbackJobPeriod, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down – exit gracefully.
                break;
            }
            catch (Exception ex)
            {
                _diagnostics?.FallbackFailed();
                if (DateTime.UtcNow >= _nextWarning)
                {
                    _nextWarning = DateTime.UtcNow.AddMinutes(1);
                    _logger.LogWarning(ex, "TickerQ overdue-work scan failed; retrying after {RetryDelay}. Last successful scan: {LastSuccessUtc}.",
                        _fallbackJobPeriod, _diagnostics?.LastSuccessfulFallbackScanUtc);
                }
                try { await Task.Delay(_fallbackJobPeriod, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
    
        
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
        if (_ownsAcquiredQueue) await _acquiredQueue.StopAsync(cancellationToken).ConfigureAwait(false);
    }
    public override void Dispose()
    {
        if (_ownsAcquiredQueue) _acquiredQueue.Dispose();
        base.Dispose();
    }
}
