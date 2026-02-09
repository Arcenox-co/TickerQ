using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
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
                ScheduledFor = context.ExecutionTime,
                ServiceScope = scope
            };        
            await function.Delegate(cancellationTokenSource.Token, scope.ServiceProvider, tickerFunctionContext);
        }
    }        
}