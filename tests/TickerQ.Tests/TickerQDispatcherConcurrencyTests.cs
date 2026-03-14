using NSubstitute;
using TickerQ.Dispatcher;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class TickerQDispatcherConcurrencyTests
{
    private readonly ITickerQTaskScheduler _taskScheduler;
    private readonly ITickerExecutionTaskHandler _taskHandler;

    public TickerQDispatcherConcurrencyTests()
    {
        _taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        _taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenConcurrencyGateIsNull()
    {
        var act = () => new TickerQDispatcher(_taskScheduler, _taskHandler, null!);

        var ex = Assert.Throws<ArgumentNullException>(act);
        Assert.Equal("concurrencyGate", ex.ParamName);
    }

    [Fact]
    public async Task DispatchAsync_ZeroMaxConcurrency_DoesNotAcquireSemaphore()
    {
        var gate = new TickerFunctionConcurrencyGate();
        var dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler, gate);

        Func<CancellationToken, Task>? capturedWork = null;
        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWork = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Unlimited",
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 0,
            TimeTickerChildren = []
        };

        await dispatcher.DispatchAsync([context]);

        Assert.NotNull(capturedWork);
        await capturedWork!(CancellationToken.None);

        // No semaphore should have been created for maxConcurrency=0
        var semaphore = gate.GetSemaphoreOrNull("Unlimited", 0);
        Assert.Null(semaphore);

        await _taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithMaxConcurrency_AcquiresAndReleasesSemaphore()
    {
        var gate = new TickerFunctionConcurrencyGate();
        var dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler, gate);

        Func<CancellationToken, Task>? capturedWork = null;
        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWork = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Limited",
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 1,
            TimeTickerChildren = []
        };

        await dispatcher.DispatchAsync([context]);

        var semaphore = gate.GetSemaphoreOrNull("Limited", 1);
        Assert.NotNull(semaphore);
        Assert.Equal(1, semaphore!.CurrentCount);

        // Execute the work — semaphore should be acquired then released
        await capturedWork!(CancellationToken.None);

        // After execution completes, semaphore should be released back
        Assert.Equal(1, semaphore.CurrentCount);

        await _taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithMaxConcurrency_ReleasesSemaphoreOnException()
    {
        var gate = new TickerFunctionConcurrencyGate();
        var dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler, gate);

        _taskHandler.ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Task failed"));

        Func<CancellationToken, Task>? capturedWork = null;
        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWork = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "FailingFunc",
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 1,
            TimeTickerChildren = []
        };

        await dispatcher.DispatchAsync([context]);

        var semaphore = gate.GetSemaphoreOrNull("FailingFunc", 1);
        Assert.Equal(1, semaphore!.CurrentCount);

        // Execute the work — should throw but semaphore must still be released
        await Assert.ThrowsAsync<InvalidOperationException>(() => capturedWork!(CancellationToken.None));

        // Semaphore must be released even after exception
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DispatchAsync_DifferentFunctions_HaveIndependentSemaphores()
    {
        var gate = new TickerFunctionConcurrencyGate();
        var dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler, gate);

        var capturedWorks = new List<Func<CancellationToken, Task>>();
        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWorks.Add(ci.ArgAt<Func<CancellationToken, Task>>(0));
                return ValueTask.CompletedTask;
            });

        var contexts = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncA",
                CachedPriority = TickerTaskPriority.Normal,
                CachedMaxConcurrency = 1,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncB",
                CachedPriority = TickerTaskPriority.Normal,
                CachedMaxConcurrency = 1,
                TimeTickerChildren = []
            }
        };

        await dispatcher.DispatchAsync(contexts);

        var semaphoreA = gate.GetSemaphoreOrNull("FuncA", 1);
        var semaphoreB = gate.GetSemaphoreOrNull("FuncB", 1);

        Assert.NotSame(semaphoreA, semaphoreB);
    }

    [Fact]
    public async Task DispatchAsync_MaxConcurrencyOne_SerializesExecution()
    {
        var gate = new TickerFunctionConcurrencyGate();
        var dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler, gate);

        var capturedWorks = new List<Func<CancellationToken, Task>>();
        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWorks.Add(ci.ArgAt<Func<CancellationToken, Task>>(0));
                return ValueTask.CompletedTask;
            });

        var concurrentCount = 0;
        var maxObserved = 0;
        var executionOrder = new List<Guid>();

        _taskHandler.ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(async ci =>
            {
                var ctx = ci.ArgAt<InternalFunctionContext>(0);
                var current = Interlocked.Increment(ref concurrentCount);
                InterlockedMax(ref maxObserved, current);
                lock (executionOrder) { executionOrder.Add(ctx.TickerId); }
                await Task.Delay(30);
                Interlocked.Decrement(ref concurrentCount);
            });

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        var contexts = new[]
        {
            new InternalFunctionContext { TickerId = id1, FunctionName = "Serial", CachedMaxConcurrency = 1, TimeTickerChildren = [] },
            new InternalFunctionContext { TickerId = id2, FunctionName = "Serial", CachedMaxConcurrency = 1, TimeTickerChildren = [] },
            new InternalFunctionContext { TickerId = id3, FunctionName = "Serial", CachedMaxConcurrency = 1, TimeTickerChildren = [] }
        };

        await dispatcher.DispatchAsync(contexts);

        Assert.Equal(3, capturedWorks.Count);

        // Execute all work items concurrently — semaphore should serialize them
        await Task.WhenAll(capturedWorks.Select(w => Task.Run(() => w(CancellationToken.None))));

        Assert.Equal(1, maxObserved);
        Assert.Equal(3, executionOrder.Count);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}
