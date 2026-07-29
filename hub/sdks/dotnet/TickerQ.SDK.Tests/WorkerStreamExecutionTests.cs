using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Google.Protobuf;
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
    public async Task TargetedCancellation_StopsOnlyMatchingRequest_WhenOlderGenerationRegistersLate()
    {
        var handler = new OverlappingHandler();
        var service = CreateService(handler);
        var tickerId = Guid.NewGuid();
        var olderRequest = Request(tickerId);
        var newerRequest = Request(tickerId);

        var newer = service.ExecuteFunctionAsync(newerRequest, tickerId, CancellationToken.None);
        await handler.WaitForCallAsync(1);
        var older = service.ExecuteFunctionAsync(olderRequest, tickerId, CancellationToken.None);
        await handler.WaitForCallAsync(2);

        InvokeCancel(service, tickerId, newerRequest.RequestId);

        var newerResult = await newer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(newerResult.Cancelled);
        Assert.False(older.IsCompleted);

        handler.Complete(1);
        var olderResult = await older.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(olderResult.Success);
    }

    [Fact]
    public async Task DetachedExecutionReorder_KeepsArrivalGenerationRegistration()
    {
        var handler = new OverlappingHandler();
        var service = CreateService(handler);
        var detached = new List<Func<Task>>();
        service.BackgroundTaskRunner = work =>
        {
            detached.Add(work);
            return Task.CompletedTask;
        };
        var tickerId = Guid.NewGuid();
        var olderRequest = Request(tickerId);
        var newerRequest = Request(tickerId);

        await service.HandleExecuteFunctionAsync(olderRequest, CancellationToken.None);
        await service.HandleExecuteFunctionAsync(newerRequest, CancellationToken.None);

        _ = detached[1](); // Force the newer detached body to begin first.
        await handler.WaitForCallAsync(1);
        _ = detached[0](); // The older body registers with the handler late.
        await handler.WaitForCallAsync(2);
        InvokeCancel(service, tickerId, newerRequest.RequestId);

        await handler.WaitForCancellationAsync(0).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(handler.IsCancellationRequested(1));
        handler.Complete(1);
    }

    [Fact]
    public async Task LegacyTickerOnlyCancellation_StopsAllMatchingActiveRequests()
    {
        var handler = new OverlappingHandler();
        var service = CreateService(handler);
        var tickerId = Guid.NewGuid();
        var first = service.ExecuteFunctionAsync(Request(tickerId), tickerId, CancellationToken.None);
        await handler.WaitForCallAsync(1);
        var second = service.ExecuteFunctionAsync(Request(tickerId), tickerId, CancellationToken.None);
        await handler.WaitForCallAsync(2);

        InvokeCancel(service, tickerId);

        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(5))).Cancelled);
        Assert.True((await second.WaitAsync(TimeSpan.FromSeconds(5))).Cancelled);
    }

    [Fact]
    public void LegacyCancelExecutionWireMessage_ParsesWithoutExecutionRequestId()
    {
        var legacy = new CancelExecution
        {
            RequestId = "command",
            TickerId = Guid.NewGuid().ToString()
        };

        var parsed = CancelExecution.Parser.ParseFrom(legacy.ToByteArray());

        Assert.False(parsed.HasExecutionRequestId);
        Assert.Equal(string.Empty, parsed.ExecutionRequestId);
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

    private static void InvokeCancel(
        WorkerStreamHostedService service, Guid tickerId, string? executionRequestId = null)
    {
        var method = typeof(WorkerStreamHostedService).GetMethod(
            "HandleCancelExecution", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var request = new CancelExecution { TickerId = tickerId.ToString() };
        if (executionRequestId is not null)
            request.ExecutionRequestId = executionRequestId;
        method!.Invoke(service, [request]);
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
        private readonly List<CancellationToken> _tokens = [];
        private readonly SemaphoreSlim _started = new(0);

        public Task ExecuteTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<TickerWorkerExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_calls)
            {
                _calls.Add(completion);
                _tokens.Add(cancellationToken);
            }
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

        public bool IsCancellationRequested(int index)
        {
            lock (_calls) return _tokens[index].IsCancellationRequested;
        }

        public Task WaitForCancellationAsync(int index)
        {
            CancellationToken token;
            lock (_calls) token = _tokens[index];
            if (token.IsCancellationRequested) return Task.CompletedTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => completion.TrySetResult());
            return completion.Task;
        }
    }
}
