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
            ITickerClock clock
        )
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
                    _ = Task.Factory
                        .StartNew(
                            async () => await ExecuteTaskAsync(context, tickerItem.Delegate, dueDone,
                                cancellationToken), TaskCreationOptions.LongRunning);
                else
                {
                    var taskDetails = Task.Factory.StartNew(
                        async () => await ExecuteTaskAsync(context, tickerItem.Delegate, dueDone,
                            cancellationToken)
                        , cancellationToken, TaskCreationOptions.DenyChildAttach,
                        _tickerTaskScheduler)
                        .Unwrap();

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

            var scope = ServiceProvider.CreateScope();
            
            var internalTickerManager = context.TickerId != Guid.Empty 
                ? scope.ServiceProvider.GetRequiredService<IInternalTickerManager>()
                : null;

            var cancellationTokenSource = cancellationToken != CancellationToken.None
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();

            if (context.TickerId != Guid.Empty)
                TickerCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context.FunctionName,
                    context.TickerId, context.Type, isDue);

            try
            {
                stopWatch.Start();

                async Task ExecuteDelegate(IServiceProvider scopeServiceProvider, CancellationToken scopeCancellationToken = default)
                {
                    try
                    {
                        await delegateFunction(scopeCancellationToken, scopeServiceProvider,
                            new TickerFunctionContext(context.TickerId, context.Type, context.RetryCount, isDue,
                                 () => DeleteTicker(context, internalTickerManager, scopeCancellationToken), null));
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (TerminateExecutionException)
                    {
                        throw;
                    }
                    catch
                    {
                        if (context.TickerId == Guid.Empty)
                            throw;
                        
                        if (context.RetryCount >= context.Retries) throw;

                        var retryInterval = (context.RetryIntervals != null && context.RetryIntervals.Length > 0)
                            ? (context.RetryCount < context.RetryIntervals.Length
                                ? context.RetryIntervals[context.RetryCount]
                                : context.RetryIntervals[^1])
                            : 30;

                        context.RetryCount++;

                        await internalTickerManager.UpdateTickerRetries(context, cancellationToken);

                        await Task.Delay(TimeSpan.FromSeconds(retryInterval), scopeCancellationToken);

                        await ExecuteDelegate(scopeServiceProvider, scopeCancellationToken);
                    }
                }
                
                await ExecuteDelegate(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                stopWatch.Stop();
                context.ElapsedTime = stopWatch.ElapsedMilliseconds;
                if (context.TickerId != Guid.Empty)
                {
                    context.Status = isDue ? TickerStatus.DueDone : TickerStatus.Done;
                    await internalTickerManager!.SetTickerStatus(context, cancellationToken);
                }
            }
            catch (TerminateExecutionException)
            {
            }
            catch (TaskCanceledException e)
            {
                context.ElapsedTime = stopWatch.ElapsedMilliseconds;
                context.Status = TickerStatus.Cancelled;
                if (context.TickerId != Guid.Empty)
                    await internalTickerManager!.SetTickerStatus(context, cancellationToken);

                var exceptionHandler = scope.ServiceProvider.GetService<ITickerExceptionHandler>();

                if (exceptionHandler != null)
                    await exceptionHandler.HandleCanceledExceptionAsync(e, context.TickerId, context.Type);
            }
            catch (Exception e)
            {
                context.ElapsedTime = stopWatch.ElapsedMilliseconds;
                context.ExceptionDetails = SerializeException(e);
                context.Status = TickerStatus.Failed;
                if (context.TickerId != Guid.Empty)
                    await internalTickerManager!.SetTickerStatus(context, cancellationToken);

                var exceptionHandler = scope.ServiceProvider.GetService<ITickerExceptionHandler>();

                if (exceptionHandler != null)
                    await exceptionHandler.HandleExceptionAsync(e, context.TickerId, context.Type);
            }
            finally
            {
                scope.Dispose();
                stopWatch.Reset();
                cancellationTokenSource.Dispose();
                if (context.TickerId != Guid.Empty)
                    TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
            }
        }
        
        private static async Task DeleteTicker(InternalFunctionContext context, IInternalTickerManager internalTickerManager, CancellationToken cancellationToken)
        {
           await internalTickerManager.DeleteTicker(context.TickerId, context.Type, cancellationToken);
           
           throw new TerminateExecutionException();
        }
        
        private static Exception GetRootException(Exception ex)
        {
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }

        private static string SerializeException(Exception ex)
        {
            var rootException = GetRootException(ex);
            var stackTrace = new StackTrace(rootException, true);
            var frame = stackTrace.GetFrame(0);
            
            var serialized = JsonSerializer.Serialize(new ExceptionDetailClassForSerialization
            {
                Message = ex.Message,
                StackTrace = frame.ToString(),
            });
            
            return serialized;
        }
    }
}