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
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ
{
    internal class TickerHost : BaseTicker
    {
        private readonly TickerTaskScheduler _tickerTaskScheduler;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public TickerHost(
            IServiceProvider serviceProvider,
            TickerOptionsBuilder tickerOptionsBuilder,
            ILogger<TickerHost> logger,
            ITickerClock clock)
            : base(tickerOptionsBuilder, serviceProvider, logger, clock)
        {
            _tickerTaskScheduler = new TickerTaskScheduler(tickerOptionsBuilder.MaxConcurrency);
        }

        protected override async Task OnTimerTick(
            InternalFunctionContext[] functions,
            CancellationToken cancellationToken = default, bool dueDone = false)
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
                        cancellationToken, TaskCreationOptions.DenyChildAttach, _tickerTaskScheduler).Unwrap();

                    _tickerTaskScheduler.SetQueuedTaskPriority(taskDetails.Id, tickerItem.Priority);
                }
            }

            _tickerTaskScheduler.ExecutePriorityTasks();
            _semaphoreSlim.Release();
        }

        private async Task ExecuteTaskAsync(InternalFunctionContext context,
            TickerFunctionDelegate delegateFunction,
            bool isDue,
            CancellationToken cancellationToken = default)
        {
            var stopWatch = new Stopwatch();
            var cancellationTokenSource = cancellationToken != CancellationToken.None
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();

            TickerCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context.FunctionName,
                context.TickerId, context.Type, isDue);

            Exception lastException = null;
            var success = false;

            for (var attempt = context.RetryCount; attempt <= context.Retries; attempt++)
            {
                using var scope = ServiceProvider.CreateScope();
                var scopedProvider = scope.ServiceProvider;
                var internalTickerManager = scopedProvider.GetRequiredService<IInternalTickerManager>();

                try
                {
                    if (await WaitForRetry(context, cancellationToken, attempt, cancellationTokenSource)) break;

                    stopWatch.Start();

                    await delegateFunction(cancellationTokenSource.Token, scopedProvider,
                        new TickerFunctionContext(
                            context.TickerId,
                            context.Type,
                            attempt,
                            isDue,
                            () => DeleteTicker(context, internalTickerManager!, cancellationTokenSource.Token),
                            null));

                    success = true;
                    context.RetryCount = attempt;
                    break;
                }
                catch (TaskCanceledException ex)
                {
                    context.Status = TickerStatus.Cancelled;
                    context.ElapsedTime = stopWatch.ElapsedMilliseconds;

                    var handler = scope.ServiceProvider.GetService<ITickerExceptionHandler>();
                    
                    if (handler != null)
                        await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type);

                    using var updateScope = ServiceProvider.CreateScope();
                    var updateManager = updateScope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                    await updateManager.SetTickerStatus(context, cancellationToken);
                    
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
            context.ElapsedTime = stopWatch.ElapsedMilliseconds;

            if (success)
            {
                context.Status = isDue ? TickerStatus.DueDone : TickerStatus.Done;
                using var statusScope = ServiceProvider.CreateScope();
                var manager = statusScope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                await manager.SetTickerStatus(context, cancellationToken);
            }
            else if (lastException != null)
            {
                context.Status = TickerStatus.Failed;
                context.ExceptionDetails = SerializeException(lastException);

                using var handlerScope = ServiceProvider.CreateScope();
                var handler = handlerScope.ServiceProvider.GetService<ITickerExceptionHandler>();
                if (handler != null)
                    await handler.HandleExceptionAsync(lastException, context.TickerId, context.Type);

                var manager = handlerScope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                await manager.SetTickerStatus(context, cancellationToken);
            }

            cancellationTokenSource.Dispose();
            TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
        }

        private async Task<bool> WaitForRetry(InternalFunctionContext context, CancellationToken cancellationToken, int attempt,
            CancellationTokenSource cancellationTokenSource)
        {
            if (attempt == 0) return false;
            
            if (attempt >= context.Retries)
                return true;

            context.RetryCount = attempt + 1;

            using var retryScope = ServiceProvider.CreateScope();
                    
            var retryManager = retryScope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                    
            await retryManager.UpdateTickerRetries(context, cancellationToken);
                    
            var retryInterval = (context.RetryIntervals?.Length > 0)
                ? (attempt < context.RetryIntervals.Length
                    ? context.RetryIntervals[attempt]
                    : context.RetryIntervals[^1])
                : 30;

            await Task.Delay(TimeSpan.FromSeconds(retryInterval), cancellationTokenSource.Token);

            return false;
        }

        private static async Task DeleteTicker(InternalFunctionContext context,
            IInternalTickerManager internalTickerManager, CancellationToken cancellationToken)
        {
            await internalTickerManager.DeleteTicker(context.TickerId, context.Type, cancellationToken);
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