using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Promises;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Checks promises every <see cref="TickerPromiseOptions.CheckInterval"/> once the scheduler is activated. Registered
/// only where background services run; dashboard-only nodes read the stored results. Each promise's lease decides
/// which node checks it, so running this on every node checks each promise once per interval.
/// </summary>
internal sealed class TickerQPromiseBackgroundService : TickerQBackgroundService
{
    private readonly ITickerPromiseService _promises;
    private readonly TickerPromiseOptions _options;
    private readonly ITickerQActivationGate _activationGate;
    private readonly ILogger<TickerQPromiseBackgroundService> _logger;

    public TickerQPromiseBackgroundService(
        ITickerPromiseService promises,
        TickerPromiseOptions options,
        ITickerQActivationGate activationGate,
        ILogger<TickerQPromiseBackgroundService> logger)
        : base(logger)
    {
        _promises = promises;
        _options = options;
        _activationGate = activationGate;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_promises.HasPromises)
            return;
        await _activationGate.WaitForActivationAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _promises.CheckDueAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(_options.CheckInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TickerQ promise checks failed; retrying next interval");
                await Task.Delay(_options.CheckInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
