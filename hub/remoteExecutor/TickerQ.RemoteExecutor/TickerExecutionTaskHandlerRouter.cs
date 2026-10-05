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

        // Graph traversal, lifecycle persistence, cancellation, and instrumentation live in
        // Core's handler. Qualified remote names affect how that handler executes one leaf,
        // but must not route the root around graph orchestration.
        var localHandler = ResolveLocalHandler();
        if (localHandler != null)
        {
            return localHandler.ExecuteTaskAsync(context, isDue, cancellationToken);
        }

        // Kept only as a compatibility fallback for hosts without Core's handler.
        return _remoteHandler.ExecuteTaskAsync(context, isDue, cancellationToken);
    }

    public Task ExecuteRegisteredTaskAsync(
        InternalFunctionContext context,
        bool isDue,
        CancellationTokenSource registeredSource,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (registeredSource == null)
            throw new ArgumentNullException(nameof(registeredSource));

        var localHandler = ResolveLocalHandler();
        if (localHandler != null)
        {
            return localHandler.ExecuteRegisteredTaskAsync(
                context, isDue, registeredSource, cancellationToken);
        }

        return ((ITickerExecutionTaskHandler)_remoteHandler).ExecuteRegisteredTaskAsync(
            context, isDue, registeredSource, cancellationToken);
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
