using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;

namespace TickerQ.BackgroundServices;

internal class TickerQStartupValidator : IHostedService
{
    private readonly TickerExecutionContext _executionContext;
    private readonly TickerQInitializerHostedService _initializer;
    private readonly ILogger<TickerQStartupValidator> _logger;

    public TickerQStartupValidator(
        TickerExecutionContext executionContext,
        TickerQInitializerHostedService initializer,
        ILogger<TickerQStartupValidator> logger)
    {
        _executionContext = executionContext;
        _initializer = initializer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
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
