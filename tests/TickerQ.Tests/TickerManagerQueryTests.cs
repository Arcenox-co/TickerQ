using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;

namespace TickerQ.Tests;

/// <summary>Reading, listing, and deleting tickers by id, filter, and function on the built-in manager.</summary>
[Collection("TickerFunctionProviderState")]
public sealed class TickerManagerQueryTests : IDisposable
{
    public sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker> { }
    public sealed class TestCronTicker : CronTickerEntity { }

    public sealed class QueryReportJob : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class QueryCleanupJob : ITickerFunction
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

    public TickerManagerQueryTests()
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [nameof(QueryReportJob)] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0),
                [nameof(QueryCleanupJob)] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();

        _clock.UtcNow.Returns(Now);
        _schedulerOptions.BindRuntimeActivationScope("query-" + Guid.NewGuid().ToString("N")[..8], 1,
            schedulerEnabled: false);
        var services = new ServiceCollection().AddSingleton(_clock).AddSingleton(_schedulerOptions).BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker>(services);
        var manager = new TickerManager<TestTimeTicker, TestCronTicker>(
            _provider,
            Substitute.For<ITickerQHostScheduler>(),
            _clock,
            Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(),
            Substitute.For<ITickerQDispatcher>(),
            _schedulerOptions,
            new TickerQActivationGate());
        _time = manager;
        _cron = manager;
    }

    public void Dispose() => TickerFunctionProvider.Build();

    private async Task<TestTimeTicker> Add(string function, TestTimeTicker child = null)
    {
        var ticker = new TestTimeTicker { Function = function, ExecutionTime = Now.AddHours(1) };
        if (child != null) ticker.Children.Add(child);
        var result = await _time.AddAsync(ticker);
        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        return result.Result;
    }

    private async Task<TestCronTicker> AddCron(string function, string expression = "0 0 * * * *")
    {
        var result = await _cron.AddAsync(new TestCronTicker { Function = function, Expression = expression });
        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        return result.Result;
    }

    [Fact]
    public async Task Get_returns_a_ticker_with_its_steps_and_null_when_missing()
    {
        var chain = await Add(nameof(QueryReportJob), new TestTimeTicker { Function = nameof(QueryCleanupJob) });

        var read = await _time.GetAsync(chain.Id);

        Assert.NotNull(read);
        Assert.Equal(nameof(QueryReportJob), read.Function);
        Assert.Equal(nameof(QueryCleanupJob), Assert.Single(read.Children).Function);
        Assert.Null(await _time.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task List_returns_roots_with_steps_and_filters_by_predicate_or_function()
    {
        var report = await Add(nameof(QueryReportJob));
        var cleanupChain = await Add(nameof(QueryCleanupJob), new TestTimeTicker { Function = nameof(QueryReportJob) });

        var all = await _time.ListAsync();
        Assert.Equal(new[] { report.Id, cleanupChain.Id }.Order(), all.Select(x => x.Id).Order());
        Assert.Single(all.Single(x => x.Id == cleanupChain.Id).Children);

        // A QueryReportJob step inside another chain is not listed on its own.
        var reports = await _time.ListAsync<QueryReportJob>();
        Assert.Equal(report.Id, Assert.Single(reports).Id);

        var filtered = await _time.ListAsync(x => x.Function == nameof(QueryCleanupJob));
        Assert.Equal(cleanupChain.Id, Assert.Single(filtered).Id);
        Assert.Empty(await _time.ListAsync(x => x.Function == "Missing"));
    }

    [Fact]
    public async Task Delete_by_function_filter_removes_that_functions_roots_with_their_steps()
    {
        var report = await Add(nameof(QueryReportJob));
        var reportChain = await Add(nameof(QueryReportJob), new TestTimeTicker { Function = nameof(QueryCleanupJob) });
        var cleanupChain = await Add(nameof(QueryCleanupJob), new TestTimeTicker { Function = nameof(QueryReportJob) });

        var result = await _time.DeleteAsync(x => x.Function == nameof(QueryReportJob));

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        Assert.True(result.AffectedRows >= 2);
        Assert.Null(await _provider.GetTimeTickerById(report.Id));
        Assert.Null(await _provider.GetTimeTickerById(reportChain.Id));
        Assert.Null(await _provider.GetTimeTickerById(Assert.Single(reportChain.Children).Id));
        // Another function's chain is kept, including its QueryReportJob step.
        var kept = await _provider.GetTimeTickerById(cleanupChain.Id);
        Assert.NotNull(kept);
        Assert.Single(kept.Children);
        Assert.Equal(0, (await _time.DeleteAsync(x => x.Function == nameof(QueryReportJob))).AffectedRows);
    }

    [Fact]
    public async Task Delete_by_filter_keeps_a_restricted_chain_with_unfinished_steps()
    {
        var plain = await Add(nameof(QueryReportJob));
        var restricted = await Add(nameof(QueryReportJob), new TestTimeTicker { Function = nameof(QueryCleanupJob) });
        Assert.True((await _time.SetDeleteBehaviorAsync(restricted.Id, ChainDeleteBehavior.Restrict)).IsSucceeded);

        var result = await _time.DeleteAsync(_ => true);

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        Assert.Null(await _provider.GetTimeTickerById(plain.Id));
        Assert.NotNull(await _provider.GetTimeTickerById(restricted.Id));
    }

    [Fact]
    public async Task Delete_by_filter_rejects_a_null_filter()
    {
        var result = await _time.DeleteAsync((System.Linq.Expressions.Expression<Func<TestTimeTicker, bool>>)null!);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
    }

    [Fact]
    public async Task A_clock_registered_after_AddTickerQ_is_what_the_managers_treat_as_now()
    {
        var mocked = new DateTime(2025, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(mocked);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options => options
            .UseApplicationNamespace("clock-" + Guid.NewGuid().ToString("N")[..8])
            .DisableBackgroundServices());
        services.AddSingleton(clock);
        await using var provider = services.BuildServiceProvider();
        var cron = provider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var time = provider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();

        var hourly = await cron.AddAsync(new CronTickerEntity { Function = nameof(QueryReportJob), Expression = "0 0 * * * *" });
        var job = await time.AddAsync(new TimeTickerEntity { Function = nameof(QueryReportJob) });

        Assert.True(hourly.IsSucceeded, hourly.Exception?.ToString());
        Assert.Equal([mocked.AddHours(1), mocked.AddHours(2)],
            (await cron.GetNextOccurrencesAsync(hourly.Result.Id, 2)).EnsureSuccess().Result);
        Assert.Equal(mocked, job.EnsureSuccess().Result.ExecutionTime);
    }

    [Fact]
    public async Task Cron_get_and_list_read_by_id_filter_and_function()
    {
        var report = await AddCron(nameof(QueryReportJob));
        var cleanup = await AddCron(nameof(QueryCleanupJob), "0 30 * * * *");

        Assert.Equal("0 0 * * * *", (await _cron.GetAsync(report.Id))!.Expression);
        Assert.Null(await _cron.GetAsync(Guid.NewGuid()));
        Assert.Equal(new[] { report.Id, cleanup.Id }.Order(), (await _cron.ListAsync()).Select(x => x.Id).Order());
        Assert.Equal(cleanup.Id, Assert.Single(await _cron.ListAsync<QueryCleanupJob>()).Id);
        Assert.Equal(report.Id, Assert.Single(await _cron.ListAsync(x => x.Expression == "0 0 * * * *")).Id);
    }

    [Fact]
    public async Task Cron_delete_by_filter_keeps_seeded_crons()
    {
        var report = await AddCron(nameof(QueryReportJob));
        var cleanup = await AddCron(nameof(QueryCleanupJob));
        var seeded = new TestCronTicker
        {
            Id = Guid.NewGuid(), Function = nameof(QueryReportJob), Expression = "0 15 * * * *", SeedKey = "seed:QueryReportJob"
        };
        Assert.Equal(1, await _provider.InsertCronTickers([seeded], CancellationToken.None));

        var byFunction = await _cron.DeleteAsync(x => x.Function == nameof(QueryReportJob));

        Assert.True(byFunction.IsSucceeded, byFunction.Exception?.ToString());
        Assert.Equal(1, byFunction.AffectedRows);
        Assert.Null(await _provider.GetCronTickerById(report.Id, CancellationToken.None));
        Assert.NotNull(await _provider.GetCronTickerById(seeded.Id, CancellationToken.None));

        var all = await _cron.DeleteAsync(_ => true);

        Assert.Equal(1, all.AffectedRows);
        Assert.Null(await _provider.GetCronTickerById(cleanup.Id, CancellationToken.None));
        Assert.Equal(seeded.Id, Assert.Single(await _cron.ListAsync()).Id);
    }
}
