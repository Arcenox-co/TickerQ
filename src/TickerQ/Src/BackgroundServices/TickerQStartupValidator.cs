using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Licensing;

namespace TickerQ.BackgroundServices;

internal class TickerQStartupValidator : IHostedService
{
    private readonly TickerExecutionContext _executionContext;
    private readonly TickerQInitializerHostedService _initializer;
    private readonly TickerQLicenseStateProvider _licenseState;
    private readonly ILogger<TickerQStartupValidator> _logger;

    public TickerQStartupValidator(
        TickerExecutionContext executionContext,
        TickerQInitializerHostedService initializer,
        TickerQLicenseStateProvider licenseState,
        ILogger<TickerQStartupValidator> logger)
    {
        _executionContext = executionContext;
        _initializer = initializer;
        _licenseState = licenseState;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // When the offline license blocks execution the initializer intentionally does nothing, so the
        // usual "UseTickerQ not called" / "no functions" diagnostics would be misleading noise. The license
        // hosted service is the single clear warning in that case.
        if (!_licenseState.ExecutionAllowed)
            return Task.CompletedTask;

        if (!_initializer.InitializationRequested)
        {
            const string message = "TickerQ — UseTickerQ() was not called. Call app.UseTickerQ() before app.Run() to initialize the scheduler.";
            _logger.LogWarning(message);
            _executionContext.NotifyCoreAction?.Invoke(message, CoreNotifyActionType.NotifyHostExceptionMessage);
        }
        else if (TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            const string message = "TickerQ — No ticker functions registered. Ensure you have methods decorated with [TickerFunction].";
            _logger.LogWarning(message);
            _executionContext.NotifyCoreAction?.Invoke(message, CoreNotifyActionType.NotifyHostExceptionMessage);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
