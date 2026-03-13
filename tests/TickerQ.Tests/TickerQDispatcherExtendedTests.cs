using NSubstitute;
using TickerQ.Dispatcher;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class TickerQDispatcherExtendedTests
{
    private readonly ITickerQTaskScheduler _taskScheduler;
    private readonly ITickerExecutionTaskHandler _taskHandler;
    private readonly TickerQDispatcher _dispatcher;

    public TickerQDispatcherExtendedTests()
    {
        _taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        _taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        _dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler);
    }

    [Fact]
    public async Task DispatchAsync_WithThreeContexts_CallsQueueAsyncForEach()
    {
        var contexts = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncA",
                CachedPriority = TickerTaskPriority.Normal,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncB",
                CachedPriority = TickerTaskPriority.High,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncC",
                CachedPriority = TickerTaskPriority.Low,
                TimeTickerChildren = []
            }
        };

        await _dispatcher.DispatchAsync(contexts);

        await _taskScheduler.Received(3).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TickerTaskPriority.Low)]
    [InlineData(TickerTaskPriority.Normal)]
    [InlineData(TickerTaskPriority.High)]
    [InlineData(TickerTaskPriority.LongRunning)]
    public async Task DispatchAsync_EachPriorityValue_IsPassedThroughToQueueAsync(TickerTaskPriority priority)
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "PriorityFunc",
            CachedPriority = priority,
            TimeTickerChildren = []
        };

        await _dispatcher.DispatchAsync([context]);

        await _taskScheduler.Received(1).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Is(priority),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_MultiplePriorities_EachContextUsesOwnPriority()
    {
        var contexts = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "HighFunc",
                CachedPriority = TickerTaskPriority.High,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "LowFunc",
                CachedPriority = TickerTaskPriority.Low,
                TimeTickerChildren = []
            }
        };

        await _dispatcher.DispatchAsync(contexts);

        await _taskScheduler.Received(1).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Is(TickerTaskPriority.High),
            Arg.Any<CancellationToken>());

        await _taskScheduler.Received(1).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Is(TickerTaskPriority.Low),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenQueueAsyncThrows_ExceptionPropagates()
    {
        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("Scheduler is disposed"));

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Func1",
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _dispatcher.DispatchAsync([context]));

        Assert.Equal("Scheduler is disposed", ex.Message);
    }

    [Fact]
    public async Task DispatchAsync_WhenQueueAsyncThrowsOnSecondContext_FirstIsQueuedAndExceptionPropagates()
    {
        var callCount = 0;

        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                callCount++;
                if (callCount == 2)
                    throw new InvalidOperationException("Failed on second");
                return ValueTask.CompletedTask;
            });

        var contexts = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "Func1",
                CachedPriority = TickerTaskPriority.Normal,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "Func2",
                CachedPriority = TickerTaskPriority.Normal,
                TimeTickerChildren = []
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _dispatcher.DispatchAsync(contexts));

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task DispatchAsync_ContextWithChildren_QueuesParentContext()
    {
        var childContext = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "ChildFunc",
            CachedPriority = TickerTaskPriority.Low,
            TimeTickerChildren = []
        };

        var parentContext = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "ParentFunc",
            CachedPriority = TickerTaskPriority.High,
            TimeTickerChildren = [childContext]
        };

        await _dispatcher.DispatchAsync([parentContext]);

        // The dispatcher queues only the top-level contexts; children are part of the context object
        // passed to ExecuteTaskAsync, not dispatched separately.
        await _taskScheduler.Received(1).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Is(TickerTaskPriority.High),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_ContextWithChildren_PassesContextIncludingChildrenToTaskHandler()
    {
        var childContext = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "ChildFunc",
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        var parentContext = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "ParentFunc",
            CachedPriority = TickerTaskPriority.High,
            TimeTickerChildren = [childContext]
        };

        // Capture the work delegate so we can invoke it
        Func<CancellationToken, Task>? capturedWork = null;

        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                capturedWork = callInfo.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        await _dispatcher.DispatchAsync([parentContext]);

        Assert.NotNull(capturedWork);

        // Invoke the captured work item to trigger ExecuteTaskAsync
        await capturedWork!(CancellationToken.None);

        await _taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Is<InternalFunctionContext>(ctx =>
                ctx.TickerId == parentContext.TickerId &&
                ctx.TimeTickerChildren.Count == 1 &&
                ctx.TimeTickerChildren[0].FunctionName == "ChildFunc"),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_CancellationToken_IsPassedToQueueAsync()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Func1",
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        await _dispatcher.DispatchAsync([context], token);

        await _taskScheduler.Received(1).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Is(token));
    }

    [Fact]
    public async Task DispatchAsync_WorkDelegate_PassesCancellationTokenToExecuteTaskAsync()
    {
        using var cts = new CancellationTokenSource();
        var workToken = cts.Token;

        Func<CancellationToken, Task>? capturedWork = null;

        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                capturedWork = callInfo.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Func1",
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        await _dispatcher.DispatchAsync([context]);

        Assert.NotNull(capturedWork);

        // Invoke the work delegate with a specific cancellation token
        await capturedWork!(workToken);

        // The work delegate should pass the ct parameter (workToken) to ExecuteTaskAsync
        await _taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            false,
            Arg.Is(workToken));
    }

    [Fact]
    public async Task DispatchAsync_WorkDelegate_PassesIsDueAsFalse()
    {
        Func<CancellationToken, Task>? capturedWork = null;

        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                capturedWork = callInfo.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Func1",
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        await _dispatcher.DispatchAsync([context]);

        Assert.NotNull(capturedWork);
        await capturedWork!(CancellationToken.None);

        await _taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            Arg.Is(false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_ConcurrentCalls_DoNotInterfere()
    {
        var queuedPriorities = new System.Collections.Concurrent.ConcurrentBag<TickerTaskPriority>();

        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                var priority = callInfo.ArgAt<TickerTaskPriority>(1);
                queuedPriorities.Add(priority);
                return ValueTask.CompletedTask;
            });

        var contextsA = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncA1",
                CachedPriority = TickerTaskPriority.High,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncA2",
                CachedPriority = TickerTaskPriority.High,
                TimeTickerChildren = []
            }
        };

        var contextsB = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncB1",
                CachedPriority = TickerTaskPriority.Low,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncB2",
                CachedPriority = TickerTaskPriority.Low,
                TimeTickerChildren = []
            }
        };

        var contextsC = new[]
        {
            new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = "FuncC1",
                CachedPriority = TickerTaskPriority.Normal,
                TimeTickerChildren = []
            }
        };

        // Dispatch all concurrently
        await Task.WhenAll(
            _dispatcher.DispatchAsync(contextsA),
            _dispatcher.DispatchAsync(contextsB),
            _dispatcher.DispatchAsync(contextsC));

        // All 5 contexts should have been queued
        Assert.Equal(5, queuedPriorities.Count);
        Assert.Equal(2, queuedPriorities.Count(p => p == TickerTaskPriority.High));
        Assert.Equal(2, queuedPriorities.Count(p => p == TickerTaskPriority.Low));
        Assert.Equal(1, queuedPriorities.Count(p => p == TickerTaskPriority.Normal));
    }

    [Fact]
    public async Task DispatchAsync_WorkDelegate_ReceivesCorrectContextPerItem()
    {
        var capturedContextIds = new List<Guid>();

        _taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                // Execute the work delegate immediately to capture which context it uses
                var work = callInfo.ArgAt<Func<CancellationToken, Task>>(0);
                work(CancellationToken.None).GetAwaiter().GetResult();
                return ValueTask.CompletedTask;
            });

        _taskHandler.ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                var ctx = callInfo.ArgAt<InternalFunctionContext>(0);
                capturedContextIds.Add(ctx.TickerId);
                return Task.CompletedTask;
            });

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        var contexts = new[]
        {
            new InternalFunctionContext
            {
                TickerId = id1,
                FunctionName = "Func1",
                CachedPriority = TickerTaskPriority.Normal,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = id2,
                FunctionName = "Func2",
                CachedPriority = TickerTaskPriority.High,
                TimeTickerChildren = []
            },
            new InternalFunctionContext
            {
                TickerId = id3,
                FunctionName = "Func3",
                CachedPriority = TickerTaskPriority.Low,
                TimeTickerChildren = []
            }
        };

        await _dispatcher.DispatchAsync(contexts);

        Assert.Equal(3, capturedContextIds.Count);
        Assert.Equal(id1, capturedContextIds[0]);
        Assert.Equal(id2, capturedContextIds[1]);
        Assert.Equal(id3, capturedContextIds[2]);
    }
}
