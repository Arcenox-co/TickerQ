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

        // Routing rule: the function NAME determines the path, not RemoteFunctionRegistry.
        //
        //   - Bare names (no '@') came from local source-gen [TickerFunction] declarations.
        //     The cron ticker rows source-gen creates store the bare name too. Route to
        //     the local handler — it owns the retry loop, status transitions, and
        //     SdkOfflineSkipException handling.
        //   - Qualified names ("X@node") came from remote SDK function-sync. Route to
        //     the remote handler — single dispatch, SDK runs its own retry loop, scheduler
        //     just awaits the ExecutionResult.
        //
        // The previous shape branched on RemoteFunctionRegistry.IsRemote(name), which
        // returns true for any bare name a remote SDK also registered — even when a local
        // source-gen function with that bare name exists. That sent local cron occurrences
        // down the remote handler's path, which doesn't update status on the success branch,
        // leaving them InProgress forever.
        var isQualifiedRemote = !string.IsNullOrEmpty(context.FunctionName)
                                && context.FunctionName.Contains('@');

        if (!isQualifiedRemote)
        {
            var localHandler = ResolveLocalHandler();
            if (localHandler != null)
            {
                return localHandler.ExecuteTaskAsync(context, isDue, cancellationToken);
            }
        }

        // Qualified name → remote dispatch. Also the fallback when no local handler is
        // registered (shouldn't happen in normal setups but kept for symmetry).
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
