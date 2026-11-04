using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// A cancelled execution names its cause in the log line, the log scope, and the stored exception
/// details, so a manual cancel reads differently from one the application or the node caused.
/// </summary>
[Collection("TickerCancellationTokenState")]
public sealed class TickerCancellationCauseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly IInternalTickerManager _manager = Substitute.For<IInternalTickerManager>();
    private readonly ITickerQInstrumentation _instrumentation = Substitute.For<ITickerQInstrumentation>();
    private readonly TickerExecutionStopAbandonmentTests.SlotScheduler _scheduler = new();
    private readonly ScopeCapturingLogger _logger = new();

    [Theory]
    [InlineData(nameof(TickerCancellationCause.Request))]
    [InlineData(nameof(TickerCancellationCause.LeaseLost))]
    public async Task RunningJob_CancelledByARequest_RecordsItsCause(string causeName)
    {
        var cause = Enum.Parse<TickerCancellationCause>(causeName);
        var context = Context();
        using var source = new CancellationTokenSource();
        var execution = RunUntilStarted(context, source, out var started);
        await started.WaitAsync(Timeout);

        TickerCancellationTokenManager.RequestCancellation(source, cause);
        await execution.WaitAsync(Timeout);

        AssertCancelledWithCause(context, TickerCancellationReasons.WhileRunningBecause(cause), cause);
    }

    [Fact]
    public async Task JobThatCancelsItself_RecordsTheFunctionAsTheCause()
    {
        var context = Context();
        context.CachedDelegate = async (cancellationToken, _, functionContext) =>
        {
            functionContext.RequestCancellation();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        };
        using var source = new CancellationTokenSource();

        await Execute(context, source).WaitAsync(Timeout);

        AssertCancelledWithCause(context,
            TickerCancellationReasons.WhileRunningBecause(TickerCancellationCause.Function), TickerCancellationCause.Function);
    }

    [Fact]
    public async Task JobThatThrowsItsOwnCancellation_IsNotMistakenForARequest()
    {
        var context = Context();
        // An HttpClient timeout looks like this: a cancellation nothing in TickerQ asked for.
        context.CachedDelegate = (_, _, _) => throw new TaskCanceledException("The request timed out.");
        using var source = new CancellationTokenSource();

        await Execute(context, source).WaitAsync(Timeout);

        AssertCancelledWithCause(context,
            TickerCancellationReasons.WhileRunningBecause(TickerCancellationCause.FunctionException),
            TickerCancellationCause.FunctionException);
        Assert.Equal("The request timed out.", Details(context).GetProperty("Message").GetString());
    }

    [Fact]
    public async Task JobCancelledOnRequestBeforeItStarts_SaysSo()
    {
        var context = Context();
        using var source = new CancellationTokenSource();
        TickerCancellationTokenManager.RequestCancellation(source);

        await Execute(context, source).WaitAsync(Timeout);

        var reason = TickerCancellationReasons.BeforeExecutionBecause(TickerCancellationCause.Request);
        Assert.Equal(TickerStatus.Cancelled, context.Status);
        Assert.Equal(reason, context.ExceptionDetails);
        _instrumentation.Received(1).LogJobCancelled(context.TickerId, context.FunctionName, reason);
        Assert.Contains(TickerCancellationCause.Request.ToString(), _logger.Causes);
    }

    [Fact]
    public async Task UnattributedCancellation_KeepsTheExistingReasonAndShape()
    {
        var context = Context();
        using var source = new CancellationTokenSource();
        var execution = RunUntilStarted(context, source, out var started);
        await started.WaitAsync(Timeout);

        source.Cancel();
        await execution.WaitAsync(Timeout);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
        Assert.False(Details(context).TryGetProperty("Reason", out _));
        _instrumentation.Received(1).LogJobCancelled(context.TickerId, context.FunctionName, TickerCancellationReasons.WhileRunning);
        Assert.Empty(_logger.Causes);
    }

    [Fact]
    public void LifecycleLog_ShowsTheCause_NotTheGenericExceptionMessage()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var store = Substitute.For<ITickerExecutionLogStore>();
        var lines = new List<TickerExecutionLogLine>();
        store.When(x => x.Add(Arg.Any<TickerExecutionLogLine>())).Do(call => lines.Add(call.Arg<TickerExecutionLogLine>()));
        var reason = TickerCancellationReasons.WhileRunningBecause(TickerCancellationCause.Request);
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(), FunctionName = "CancellationCause", Status = TickerStatus.Cancelled,
            ExceptionDetails = JsonSerializer.Serialize(new { Type = "System.Threading.Tasks.TaskCanceledException",
                Message = "A task was canceled.", Reason = reason }),
            ParametersToUpdate = [nameof(InternalFunctionContext.Status)]
        };

        new TickerLifecycleLogger(store, new ExecutionLogOptions(), clock).Committed(context);

        var line = Assert.Single(lines);
        Assert.EndsWith(reason, line.Message);
    }

    private void AssertCancelledWithCause(InternalFunctionContext context, string reason, TickerCancellationCause cause)
    {
        Assert.Equal(TickerStatus.Cancelled, context.Status);
        var details = Details(context);
        Assert.Equal(reason, details.GetProperty("Reason").GetString());
        // The exception itself is still recorded alongside the cause.
        Assert.False(string.IsNullOrEmpty(details.GetProperty("Type").GetString()));
        _instrumentation.Received(1).LogJobCancelled(context.TickerId, context.FunctionName, reason);
        Assert.Equal([cause.ToString()], _logger.Causes);
    }

    private static JsonElement Details(InternalFunctionContext context)
        => JsonDocument.Parse(context.ExceptionDetails).RootElement;

    private Task RunUntilStarted(InternalFunctionContext context, CancellationTokenSource source, out Task started)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.CachedDelegate = async (cancellationToken, _, _) =>
        {
            signal.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        };
        started = signal.Task;
        return Execute(context, source);
    }

    private Task Execute(InternalFunctionContext context, CancellationTokenSource source)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITickerQTaskScheduler>(_scheduler)
            .AddSingleton<ILogger<TickerExecutionTaskHandler>>(_logger)
            .BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        var handler = new TickerExecutionTaskHandler(services, clock, _instrumentation, _manager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(),
            new TickerFunctionConcurrencyGate());
        return handler.ExecuteRegisteredTaskAsync(context, false, source, source.Token);
    }

    private static InternalFunctionContext Context() => new()
    {
        TickerId = Guid.NewGuid(),
        AcquisitionToken = Guid.NewGuid(),
        Type = TickerType.TimeTicker,
        FunctionName = "CancellationCause",
        CachedDelegate = (_, _, _) => Task.CompletedTask,
    };

    /// <summary>Records the CancellationCause of every scope the handler opens.</summary>
    private sealed class ScopeCapturingLogger : ILogger<TickerExecutionTaskHandler>
    {
        internal List<string> Causes { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
                Causes.AddRange(pairs.Where(x => x.Key == "CancellationCause").Select(x => (string)x.Value));
            return new Noop();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
