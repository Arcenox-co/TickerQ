using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

public class TickerRemoteExecutionTaskHandler : ITickerExecutionTaskHandler
{
    private readonly IServiceProvider _serviceProvider;

    public TickerRemoteExecutionTaskHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ??  throw new ArgumentNullException(nameof(serviceProvider));
    }
    
    public async Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await using var scope = _serviceProvider.CreateAsyncScope();

        if (TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var function))
        {
            var tickerFunctionContext = new TickerFunctionContext
            {
                RequestCancelOperationAction = null,
                Id = context.TickerId,
                Type = context.Type,
                FunctionName = context.FunctionName,
                RetryCount = context.RetryCount,
                IsDue = isDue,
                ScheduledFor = context.ExecutionTime
            };        
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await function.Delegate(cancellationTokenSource.Token, scope.ServiceProvider, tickerFunctionContext);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await MarkFailedAsync(scope.ServiceProvider, context, ex, stopwatch.ElapsedMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task MarkFailedAsync(
        IServiceProvider serviceProvider,
        InternalFunctionContext context,
        Exception exception,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        var internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
        if (internalTickerManager == null)
            return;

        var clock = serviceProvider.GetService<ITickerClock>();

        context.SetProperty(x => x.Status, TickerStatus.Failed)
            .SetProperty(x => x.ExceptionDetails, SerializeException(exception))
            .SetProperty(x => x.ElapsedTime, elapsedMilliseconds);

        if (clock != null)
        {
            context.SetProperty(x => x.ExecutedAt, clock.UtcNow);
        }

        await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static Exception GetRootException(Exception ex)
    {
        while (ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }

    private static string SerializeException(Exception ex)
    {
        var rootException = GetRootException(ex);
        var stackTrace = new StackTrace(rootException, true);
        var frame = stackTrace.GetFrame(0);

        return JsonSerializer.Serialize(new
        {
            Message = ex.Message,
            StackTrace = frame?.ToString() ?? rootException.StackTrace
        });
    }
}        
