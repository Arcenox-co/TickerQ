using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// Outcome reads, waiting for completion, rescheduling, cancelling a function's pending work, and cron run-time
/// previews on the built-in manager.
/// </summary>
[Collection("TickerFunctionProviderState")]
public sealed class TickerManagerTrackingTests : IDisposable
{
    public sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker> { }
    public sealed class TestCronTicker : CronTickerEntity { }

    public sealed class ReportJob : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class CleanupJob : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly ITickerClock _clock = Substitute.For<ITickerClock>();
    // The in-memory store is shared per namespace, so each test gets its own.
    private readonly SchedulerOptionsBuilder _schedulerOptions = new();
    private readonly TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker> _provider;
    private readonly ITimeTickerManager<TestTimeTicker> _time;
    private readonly ICronTickerManager<TestCronTicker> _cron;

    public TickerManagerTrackingTests()
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [nameof(ReportJob)] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0),
                [nameof(CleanupJob)] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();

        _clock.UtcNow.Returns(Now);
        _schedulerOptions.BindRuntimeActivationScope("tracking-" + Guid.NewGuid().ToString("N")[..8], 1,
            schedulerEnabled: false);
        var services = new ServiceCollection().AddSingleton(_clock).AddSingleton(_schedulerOptions).BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker>(services);
        var manager = Manager(_provider);
        _time = manager;
        _cron = manager;
    }

    public void Dispose() => TickerFunctionProvider.Build();

    private TickerManager<TestTimeTicker, TestCronTicker> Manager(
        ITickerPersistenceProvider<TestTimeTicker, TestCronTicker> provider)
        => new(
            provider,
            Substitute.For<ITickerQHostScheduler>(),
            _clock,
            Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(),
            Substitute.For<ITickerQDispatcher>(),
            _schedulerOptions,
            new TickerQActivationGate());

    private async Task<TestTimeTicker> Add(string function, DateTime executionTime, TestTimeTicker child = null)
    {
        var ticker = new TestTimeTicker { Function = function, ExecutionTime = executionTime };
        if (child != null) ticker.Children.Add(child);
        var result = await _time.AddAsync(ticker);
        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        return result.Result;
    }

    [Fact]
    public async Task Outcome_is_null_for_a_missing_ticker_and_not_completed_while_pending()
    {
        Assert.Null(await _time.GetOutcomeAsync(Guid.NewGuid()));
        Assert.Null(await _cron.GetOutcomeAsync(Guid.NewGuid()));

        var ticker = await Add(nameof(ReportJob), Now.AddHours(1));
        var outcome = await _time.GetOutcomeAsync(ticker.Id);

        Assert.NotNull(outcome);
        Assert.Equal(ticker.Id, outcome!.Id);
        Assert.Equal(TickerStatus.Idle, outcome.Status);
        Assert.False(outcome.IsCompleted);
        Assert.False(outcome.HasResult);
        Assert.Throws<InvalidOperationException>(() => outcome.GetResult<int>());
    }

    [Fact]
    public async Task Waiting_returns_the_successful_outcome_with_its_result()
    {
        var id = Guid.NewGuid();
        var provider = Substitute.For<ITickerPersistenceProvider<TestTimeTicker, TestCronTicker>>();
        provider.GetTimeTickerById(id, Arg.Any<CancellationToken>()).Returns(
            new TestTimeTicker { Id = id, Status = TickerStatus.Idle },
            new TestTimeTicker { Id = id, Status = TickerStatus.InProgress },
            new TestTimeTicker { Id = id, Status = TickerStatus.Done, ExecutedAt = Now });
        provider.GetTimeTickerResultAsync(id, Arg.Any<CancellationToken>()).Returns(
            new TickerResultEnvelope(JsonSerializer.SerializeToUtf8Bytes(42), TickerResultEnvelope.CurrentVersion, "application/json"));

        var outcome = await ((ITimeTickerManager<TestTimeTicker>)Manager(provider))
            .WaitForCompletionAsync(id, TimeSpan.FromSeconds(10));

        Assert.True(outcome!.IsCompleted);
        Assert.True(outcome.IsSucceeded);
        Assert.Equal(Now, outcome.ExecutedAt);
        Assert.True(outcome.HasResult);
        Assert.Equal(42, outcome.GetResult<int>());
        // The result is only read once the stored status reports success.
        await provider.Received(1).GetTimeTickerResultAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Waiting_reports_a_failure_without_reading_a_result()
    {
        var id = Guid.NewGuid();
        var provider = Substitute.For<ITickerPersistenceProvider<TestTimeTicker, TestCronTicker>>();
        provider.GetAllCronTickerOccurrences(default!, default).ReturnsForAnyArgs(
            [new CronTickerOccurrenceEntity<TestCronTicker> { Id = id, Status = TickerStatus.Failed, ExceptionMessage = "boom" }]);

        var outcome = await ((ICronTickerManager<TestCronTicker>)Manager(provider))
            .WaitForCompletionAsync(id, TimeSpan.FromSeconds(10));

        Assert.True(outcome!.IsCompleted);
        Assert.False(outcome.IsSucceeded);
        Assert.Equal("boom", outcome.ExceptionMessage);
        Assert.False(outcome.HasResult);
        await provider.DidNotReceiveWithAnyArgs().GetCronTickerOccurrenceResultAsync(default, default);
    }

    [Fact]
    public async Task Waiting_past_the_timeout_returns_the_latest_unfinished_outcome()
    {
        var ticker = await Add(nameof(ReportJob), Now.AddHours(1));
        var watch = Stopwatch.StartNew();

        var outcome = await _time.WaitForCompletionAsync(ticker.Id, TimeSpan.FromMilliseconds(300));

        Assert.False(outcome!.IsCompleted);
        Assert.Equal(TickerStatus.Idle, outcome.Status);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
        Assert.Null(await _time.WaitForCompletionAsync(Guid.NewGuid(), TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _time.WaitForCompletionAsync(ticker.Id, TimeSpan.FromSeconds(-2)));
    }

    [Fact]
    public async Task Reschedule_moves_a_pending_root()
    {
        var ticker = await Add(nameof(ReportJob), Now.AddHours(1));

        var result = await _time.RescheduleAsync(ticker.Id, Now.AddDays(2));

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        Assert.Equal(Now.AddDays(2), (await _provider.GetTimeTickerById(ticker.Id))!.ExecutionTime);
    }

    [Fact]
    public async Task Reschedule_rejects_missing_tickers_chain_steps_and_work_that_is_no_longer_pending()
    {
        var root = await Add(nameof(ReportJob), Now.AddHours(1),
            new TestTimeTicker { Function = nameof(CleanupJob) });
        var childId = Assert.Single(root.Children).Id;
        var cancelled = await Add(nameof(ReportJob), Now.AddHours(1));
        Assert.True((await _time.CancelAsync(cancelled.Id)).IsSucceeded);

        Assert.Contains("not found", (await _time.RescheduleAsync(Guid.NewGuid(), Now)).Exception!.Message);
        Assert.Contains("chain root", (await _time.RescheduleAsync(childId, Now.AddDays(1))).Exception!.Message);
        var notPending = await _time.RescheduleAsync(cancelled.Id, Now.AddDays(1));
        Assert.IsType<TickerValidatorException>(notPending.Exception);
        Assert.Contains("Cancelled", notPending.Exception!.Message);
        Assert.Equal(Now.AddHours(1), (await _provider.GetTimeTickerById(cancelled.Id))!.ExecutionTime);
    }

    [Fact]
    public async Task Cancel_pending_cancels_that_functions_pending_jobs_and_chains()
    {
        var report = await Add(nameof(ReportJob), Now.AddHours(1));
        var reportChain = await Add(nameof(ReportJob), Now.AddHours(1), new TestTimeTicker { Function = nameof(CleanupJob) });
        var cleanupChain = await Add(nameof(CleanupJob), Now.AddHours(1), new TestTimeTicker { Function = nameof(ReportJob) });

        var result = await _time.CancelPendingAsync<ReportJob>();

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        Assert.Equal(3, result.AffectedRows);
        Assert.Equal(TickerStatus.Cancelled, (await _provider.GetTimeTickerById(report.Id))!.Status);
        Assert.Equal(TickerStatus.Cancelled, (await _provider.GetTimeTickerById(reportChain.Id))!.Status);
        Assert.Equal(TickerStatus.Cancelled,
            (await _provider.GetTimeTickerById(Assert.Single(reportChain.Children).Id))!.Status);
        // Another function's chain is left alone, including its ReportJob step.
        Assert.Equal(TickerStatus.Idle, (await _provider.GetTimeTickerById(cleanupChain.Id))!.Status);
        Assert.Equal(TickerStatus.Idle,
            (await _provider.GetTimeTickerById(Assert.Single(cleanupChain.Children).Id))!.Status);
        Assert.Equal(0, (await _time.CancelPendingAsync<ReportJob>()).AffectedRows);
    }

    [Fact]
    public async Task Next_occurrences_follow_the_cron_expression_and_are_empty_while_disabled()
    {
        var added = await _cron.AddAsync(new TestCronTicker { Function = nameof(ReportJob), Expression = "0 0 * * * *" });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var next = await _cron.GetNextOccurrencesAsync(added.Result.Id, 3);

        Assert.True(next.IsSucceeded, next.Exception?.ToString());
        Assert.Equal([Now.AddHours(1), Now.AddHours(2), Now.AddHours(3)], next.Result);

        Assert.True((await _cron.SetEnabledAsync(added.Result.Id, false)).IsSucceeded);
        Assert.Empty((await _cron.GetNextOccurrencesAsync(added.Result.Id, 3)).Result);
    }

    [Fact]
    public async Task Next_occurrences_reject_a_missing_cron_and_an_out_of_range_count()
    {
        Assert.IsType<TickerValidatorException>((await _cron.GetNextOccurrencesAsync(Guid.NewGuid(), 3)).Exception);
        Assert.IsType<ArgumentOutOfRangeException>((await _cron.GetNextOccurrencesAsync(Guid.NewGuid(), 0)).Exception);
        Assert.IsType<ArgumentOutOfRangeException>((await _cron.GetNextOccurrencesAsync(Guid.NewGuid(), 1001)).Exception);
    }
}
