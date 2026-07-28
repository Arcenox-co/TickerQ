using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using TickerQ.SDK.Infrastructure;
using TickerQ.SDK.Logging;
using TickerQ.SDK.WorkerStream;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;
using Xunit;

namespace TickerQ.SDK.Tests;

public sealed class WorkerStreamExecutionTests
{
    [Fact]
    public async Task ExecuteFunctionAsync_UsesWorkerPrimitive_AndReturnsItsFinalResult()
    {
        var envelope = new TickerResultEnvelope(
            System.Text.Encoding.UTF8.GetBytes("\"final\""), 1, "application/json");
        var handler = new RecordingHandler(
            new TickerWorkerExecutionResult(TickerStatus.Done, null, envelope));
        var service = CreateService(handler);
        var request = Request();
        request.Retries = 2;
        request.RetryCount = 1;
        request.RetryIntervalsSeconds.AddRange([3, 5]);

        var result = await service.ExecuteFunctionAsync(
            request, Guid.Parse(request.TickerId), CancellationToken.None);

        Assert.Equal(1, handler.WorkerCalls);
        Assert.Equal(0, handler.SchedulerCalls);
        Assert.NotNull(handler.Context);
        Assert.Equal(2, handler.Context!.Retries);
        Assert.Equal(1, handler.Context.RetryCount);
        Assert.Equal([3, 5], handler.Context.RetryIntervals);
        Assert.True(result.Success);
        Assert.False(result.Cancelled);
        Assert.Equal("\"final\"", result.Result.Payload.ToStringUtf8());
    }

    [Fact]
    public async Task ExecuteFunctionAsync_PreservesExplicitSuccessfulResultAbsence()
    {
        var handler = new RecordingHandler(
            new TickerWorkerExecutionResult(TickerStatus.DueDone, null, null));
        var service = CreateService(handler);
        var request = Request();
        request.IsDue = true;

        var result = await service.ExecuteFunctionAsync(
            request, Guid.Parse(request.TickerId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Result);
        Assert.Equal(1, handler.WorkerCalls);
        Assert.Equal(0, handler.SchedulerCalls);
    }

    [Theory]
    [InlineData(TickerStatus.Failed, false)]
    [InlineData(TickerStatus.Cancelled, true)]
    public async Task ExecuteFunctionAsync_MapsFailedAndCancelledTerminalOutcomes(
        TickerStatus status, bool cancelled)
    {
        var handler = new RecordingHandler(
            new TickerWorkerExecutionResult(status, "terminal error", null));
        var service = CreateService(handler);
        var request = Request();

        var result = await service.ExecuteFunctionAsync(
            request, Guid.Parse(request.TickerId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(cancelled, result.Cancelled);
        Assert.Equal("terminal error", result.Error);
        Assert.Null(result.Result);
        Assert.Equal(0, handler.SchedulerCalls);
    }

    [Fact]
    public async Task OlderSameTickerCompletion_DoesNotRemoveNewerExecutionCancellation()
    {
        var handler = new OverlappingHandler();
        var service = CreateService(handler);
        var tickerId = Guid.NewGuid();
        var olderRequest = Request(tickerId);
        var newerRequest = Request(tickerId);

        var older = service.ExecuteFunctionAsync(olderRequest, tickerId, CancellationToken.None);
        await handler.WaitForCallAsync(1);
        var newer = service.ExecuteFunctionAsync(newerRequest, tickerId, CancellationToken.None);
        await handler.WaitForCallAsync(2);

        handler.Complete(0);
        await older;
        InvokeCancel(service, tickerId);

        var newerResult = await newer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(newerResult.Cancelled);
    }

    private static WorkerStreamHostedService CreateService(ITickerExecutionTaskHandler handler)
    {
        var services = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var options = new TickerSdkOptions();
        return new WorkerStreamHostedService(
            options,
            new TickerQFunctionSyncService(options),
            services,
            new TickerExecutionLogQueue(),
            NullLogger<WorkerStreamHostedService>.Instance);
    }

    private static ExecuteFunction Request(Guid? tickerId = null) => new()
    {
        RequestId = Guid.NewGuid().ToString(),
        TickerId = (tickerId ?? Guid.NewGuid()).ToString(),
        FunctionName = "WorkerFunction@node",
        Type = (int)TickerType.TimeTicker
    };

    private static void InvokeCancel(WorkerStreamHostedService service, Guid tickerId)
    {
        var method = typeof(WorkerStreamHostedService).GetMethod(
            "HandleCancelExecution", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(service, [new CancelExecution { TickerId = tickerId.ToString() }]);
    }

    private sealed class RecordingHandler(TickerWorkerExecutionResult outcome)
        : ITickerExecutionTaskHandler
    {
        public int SchedulerCalls { get; private set; }
        public int WorkerCalls { get; private set; }
        public InternalFunctionContext? Context { get; private set; }

        public Task ExecuteTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
        {
            SchedulerCalls++;
            return Task.CompletedTask;
        }

        public Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
        {
            WorkerCalls++;
            Context = context;
            return Task.FromResult(outcome);
        }
    }

    private sealed class OverlappingHandler : ITickerExecutionTaskHandler
    {
        private readonly List<TaskCompletionSource<TickerWorkerExecutionResult>> _calls = [];
        private readonly SemaphoreSlim _started = new(0);

        public Task ExecuteTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<TickerWorkerExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_calls) _calls.Add(completion);
            _started.Release();
            return await completion.Task.WaitAsync(cancellationToken);
        }

        public async Task WaitForCallAsync(int count)
        {
            while (true)
            {
                lock (_calls)
                    if (_calls.Count >= count) return;
                await _started.WaitAsync();
            }
        }

        public void Complete(int index)
        {
            TaskCompletionSource<TickerWorkerExecutionResult> completion;
            lock (_calls) completion = _calls[index];
            completion.SetResult(new TickerWorkerExecutionResult(TickerStatus.Done, null, null));
        }
    }
}
