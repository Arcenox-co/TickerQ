using FluentAssertions;
using NSubstitute;
using TickerQ.Dispatcher;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class TickerQDispatcherTests
{
    private readonly ITickerQTaskScheduler _taskScheduler;
    private readonly ITickerExecutionTaskHandler _taskHandler;
    private readonly TickerQDispatcher _dispatcher;

    public TickerQDispatcherTests()
    {
        _taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        _taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        _dispatcher = new TickerQDispatcher(_taskScheduler, _taskHandler);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenTaskSchedulerIsNull()
    {
        var act = () => new TickerQDispatcher(null!, _taskHandler);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("taskScheduler");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenTaskHandlerIsNull()
    {
        var act = () => new TickerQDispatcher(_taskScheduler, null!);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("taskHandler");
    }

    [Fact]
    public void IsEnabled_ReturnsTrue()
    {
        _dispatcher.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_DoesNothing_WhenContextsIsNull()
    {
        await _dispatcher.DispatchAsync(null!);

        await _taskScheduler.DidNotReceive().QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_DoesNothing_WhenContextsIsEmpty()
    {
        await _dispatcher.DispatchAsync([]);

        await _taskScheduler.DidNotReceive().QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_QueuesEachContext_InTaskScheduler()
    {
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
                CachedPriority = TickerTaskPriority.High,
                TimeTickerChildren = []
            }
        };

        await _dispatcher.DispatchAsync(contexts);

        await _taskScheduler.Received(2).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_UsesContextPriority_WhenQueuing()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Func1",
            CachedPriority = TickerTaskPriority.LongRunning,
            TimeTickerChildren = []
        };

        await _dispatcher.DispatchAsync([context]);

        await _taskScheduler.Received(1).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Is(TickerTaskPriority.LongRunning),
            Arg.Any<CancellationToken>());
    }
}
