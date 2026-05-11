using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
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

        if (!TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var function))
        {
            // Function not in the local registry. Almost always means the SDK that
            // owned the qualified name disconnected and the grace window expired (we
            // unregistered the entry). Without a status update here the row sits
            // InProgress forever. Mark Skipped with the SDK-offline reason so the
            // dashboard shows the correct outcome.
            await MarkSkippedAsync(
                scope.ServiceProvider, context,
                $"Function '{context.FunctionName}' is not registered (SDK offline).",
                0, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tickerFunctionContext = new TickerFunctionContext
        {
            RequestCancelOperationAction = null,
            Id = context.TickerId,
            Type = context.Type,
            FunctionName = context.FunctionName,
            RetryCount = context.RetryCount,
            // Forward retry config from the InternalFunctionContext so the
            // remote-dispatch delegate can ship it to the SDK alongside the
            // ExecuteFunction frame — the SDK's task handler is what actually
            // runs the retry loop for remote functions.
            Retries = context.Retries,
            RetryIntervals = context.RetryIntervals,
            IsDue = isDue,
            ScheduledFor = context.ExecutionTime,
            ServiceScope = scope
        };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await function.Delegate(cancellationTokenSource.Token, scope.ServiceProvider, tickerFunctionContext);
            stopwatch.Stop();
            // Success path. Without this status update the row would sit at InProgress
            // forever — the previous shape only updated status in the catch block,
            // which meant any successful dispatch left a stuck row in the DB.
            await MarkDoneAsync(scope.ServiceProvider, context, stopwatch.ElapsedMilliseconds, isDue, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SdkOfflineSkipException ex)
        {
            stopwatch.Stop();
            // Distinct from Failed: the user's code never ran, the SDK was offline.
            // Don't burn the user's Retries budget on this.
            await MarkSkippedAsync(
                scope.ServiceProvider, context, ex.Message,
                stopwatch.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            await MarkCancelledAsync(
                scope.ServiceProvider, context, stopwatch.ElapsedMilliseconds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await MarkFailedAsync(scope.ServiceProvider, context, ex, stopwatch.ElapsedMilliseconds, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task MarkDoneAsync(
        IServiceProvider serviceProvider,
        InternalFunctionContext context,
        long elapsedMilliseconds,
        bool isDue,
        CancellationToken cancellationToken)
    {
        var internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
        if (internalTickerManager == null) return;

        var clock = serviceProvider.GetService<ITickerClock>();
        context.SetProperty(x => x.Status, isDue ? TickerStatus.DueDone : TickerStatus.Done)
            .SetProperty(x => x.ElapsedTime, elapsedMilliseconds);
        if (clock != null)
            context.SetProperty(x => x.ExecutedAt, clock.UtcNow);

        await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkSkippedAsync(
        IServiceProvider serviceProvider,
        InternalFunctionContext context,
        string reason,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        var internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
        if (internalTickerManager == null) return;

        var clock = serviceProvider.GetService<ITickerClock>();
        context.SetProperty(x => x.Status, TickerStatus.Skipped)
            .SetProperty(x => x.ExceptionDetails, reason ?? string.Empty)
            .SetProperty(x => x.ElapsedTime, elapsedMilliseconds);
        if (clock != null)
            context.SetProperty(x => x.ExecutedAt, clock.UtcNow);

        await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkCancelledAsync(
        IServiceProvider serviceProvider,
        InternalFunctionContext context,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        var internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
        if (internalTickerManager == null) return;

        var clock = serviceProvider.GetService<ITickerClock>();
        context.SetProperty(x => x.Status, TickerStatus.Cancelled)
            .SetProperty(x => x.ElapsedTime, elapsedMilliseconds);
        if (clock != null)
            context.SetProperty(x => x.ExecutedAt, clock.UtcNow);

        await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
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
