using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

internal sealed class TickerExecutionTaskHandlerRouter : ITickerExecutionTaskHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TickerRemoteExecutionTaskHandler _remoteHandler;
    private ITickerExecutionTaskHandler? _localHandler;

    public TickerExecutionTaskHandlerRouter(
        IServiceProvider serviceProvider,
        TickerRemoteExecutionTaskHandler remoteHandler)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _remoteHandler = remoteHandler ?? throw new ArgumentNullException(nameof(remoteHandler));
    }

    public Task ExecuteTaskAsync(
        InternalFunctionContext context,
        bool isDue,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (RemoteFunctionRegistry.IsRemote(context.FunctionName))
        {
            return _remoteHandler.ExecuteTaskAsync(context, isDue, cancellationToken);
        }

        var localHandler = ResolveLocalHandler();
        if (localHandler != null)
        {
            return localHandler.ExecuteTaskAsync(context, isDue, cancellationToken);
        }

        // Fallback to remote handler if no local handler is available.
        return _remoteHandler.ExecuteTaskAsync(context, isDue, cancellationToken);
    }

    private ITickerExecutionTaskHandler? ResolveLocalHandler()
    {
        if (_localHandler != null)
            return _localHandler;

        ITickerExecutionTaskHandler? candidate = null;
        foreach (var handler in _serviceProvider.GetServices<ITickerExecutionTaskHandler>())
        {
            if (ReferenceEquals(handler, this))
                continue;
            if (handler is TickerRemoteExecutionTaskHandler)
                continue;

            candidate = handler;
        }

        _localHandler = candidate;
        return _localHandler;
    }
}
