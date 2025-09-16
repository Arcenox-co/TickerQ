using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Base;
using TickerQ.Exceptions;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ
{
    internal class TickerHost : BaseTicker
    {
        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

        public TickerHost(IServiceProvider serviceProvider, TickerExecutionContext executionContext, ILogger<TickerHost> logger, ITickerClock clock)
            : base(executionContext, logger, clock, serviceProvider)
        {
        }

        protected override async Task OnTimerTick(InternalFunctionContext[] functions, bool dueDone,
            CancellationToken cancellationToken)
        {
            await _semaphoreSlim.WaitAsync(cancellationToken);

            foreach (var context in functions)
            {
                if (!TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var tickerItem))
                    continue;

                if (tickerItem.Priority == TickerTaskPriority.LongRunning)
                    _ = Task.Factory.StartNew(
                        async () => await ExecuteTaskAsync(context, tickerItem.Delegate, dueDone, cancellationToken),
                        TaskCreationOptions.LongRunning);
                else
                {
                    var taskDetails = Task.Factory.StartNew(
                        async () => await ExecuteTaskAsync(context, tickerItem.Delegate, dueDone, cancellationToken),
                        cancellationToken, TaskCreationOptions.DenyChildAttach, TickerTaskScheduler).Unwrap();

                    TickerTaskScheduler.SetQueuedTaskPriority(taskDetails.Id, tickerItem.Priority);
                }
            }

            TickerTaskScheduler.ExecutePriorityTasks();
            _semaphoreSlim.Release();
        }

        private async Task ExecuteTaskAsync(InternalFunctionContext context, TickerFunctionDelegate delegateFunction,
            bool isDue, CancellationToken cancellationToken = default)
        {
            var stopWatch = new Stopwatch();
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            TickerCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context.FunctionName,
                context.TickerId, context.Type, isDue);

            Exception lastException = null;
            var success = false;

            for (var attempt = context.RetryCount; attempt <= context.Retries; attempt++)
            {
                try
                {
                    if (await WaitForRetry(context, cancellationToken, attempt, cancellationTokenSource)) break;

                    stopWatch.Start();
                    
                    await using var scope = ServiceProvider.CreateAsyncScope();

                    await delegateFunction(cancellationTokenSource.Token, scope.ServiceProvider,
                        new TickerFunctionContext(
                            context.TickerId,
                            context.Type,
                            attempt,
                            isDue,
                            () => DeleteTicker(context, cancellationTokenSource.Token),
                            null));

                    success = true;
                    context.RetryCount = attempt;
                    break;
                }
                catch (TaskCanceledException ex)
                {
                    context.SetProperty(x => x.Status, TickerStatus.Cancelled)
                        .SetProperty(x => x.ExecutedAt, Clock.Now)
                        .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                        .SetProperty(x => x.ExceptionDetails, SerializeException(lastException));

                    var handler = ServiceProvider.GetService<ITickerExceptionHandler>();

                    if (handler != null)
                        await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type);

                    await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);

                    return;
                }
                catch (TerminateExecutionException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            stopWatch.Stop();
            
            context.SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                .SetProperty(x => x.ExecutedAt, Clock.Now);
            
            if (success)
            {
                context.SetProperty(x => x.Status, isDue ? TickerStatus.DueDone : TickerStatus.Done);
                
                await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
            }
            else if (lastException != null)
            {
                context.SetProperty(x => x.Status, TickerStatus.Failed)
                    .SetProperty(x => x.ExceptionDetails, SerializeException(lastException));

                var handler = ServiceProvider.GetService<ITickerExceptionHandler>();
                
                if (handler != null)
                    await handler.HandleExceptionAsync(lastException, context.TickerId, context.Type);
                
                await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
            }

            cancellationTokenSource.Dispose();
            TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
        }

        private async Task<bool> WaitForRetry(InternalFunctionContext context, CancellationToken cancellationToken,
            int attempt, CancellationTokenSource cancellationTokenSource)
        {
            if (attempt == 0) 
                return false;

            if (attempt >= context.Retries)
                return true;

            context.SetProperty(x => x.RetryCount, attempt + 1);
            
            await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);

            context.ResetUpdateProps();
            
            var retryInterval = (context.RetryIntervals?.Length > 0)
                ? (attempt < context.RetryIntervals.Length
                    ? context.RetryIntervals[attempt]
                    : context.RetryIntervals[^1])
                : 30;

            await Task.Delay(TimeSpan.FromSeconds(retryInterval), cancellationTokenSource.Token);

            return false;
        }

        private async Task DeleteTicker(InternalFunctionContext context, CancellationToken cancellationToken)
        {
            await InternalTickerManager.DeleteTicker(context.TickerId, context.Type, cancellationToken);
            throw new TerminateExecutionException();
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

            return JsonSerializer.Serialize(new ExceptionDetailClassForSerialization
            {
                Message = ex.Message,
                StackTrace = frame?.ToString() ?? rootException.StackTrace
            });
        }
    }
}