using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Regression: the in-memory provider clones periodic tickers when advancing schedule state
/// (UpdatePeriodicTickerAfterExecution). The clone must preserve the chain template, otherwise
/// chaining would silently stop after the first fire.
/// </summary>
[Collection("PeriodicInMemoryStaticState")]
public class PeriodicTickerInMemoryChainCloneTests
{
    private static PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity> CreateProvider()
    {
        var services = new ServiceCollection();
        return new PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity>(services.BuildServiceProvider());
    }

    [Fact]
    public async Task UpdateAfterExecution_PreservesChainTemplate_AndOverlapBehavior()
    {
        var provider = CreateProvider();

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "PollPort",
            Interval = TimeSpan.FromMinutes(1),
            ChainOverlapBehavior = ChainOverlapBehavior.Skip,
            ChainTemplate = new[]
            {
                new PeriodicChainStep { Function = "ScheduledCalculations", RunCondition = RunCondition.OnSuccess }
            }
        };

        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        await provider.UpdatePeriodicTickerAfterExecution(ticker.Id, DateTime.UtcNow, succeeded: true);

        var stored = await provider.GetPeriodicTickerById(ticker.Id, default);

        Assert.NotNull(stored);
        Assert.Equal(1, stored.ExecutionCount);
        Assert.NotNull(stored.ChainTemplate);
        Assert.Single(stored.ChainTemplate);
        Assert.Equal("ScheduledCalculations", stored.ChainTemplate[0].Function);
        Assert.Equal(ChainOverlapBehavior.Skip, stored.ChainOverlapBehavior);
    }

    /// <summary>
    /// Regression for the perpetual-refire bug: a terminal Failed occurrence must still advance
    /// LastExecutedAt (so CalculateNextExecution waits the interval instead of returning "now"),
    /// but must NOT inflate ExecutionCount, which counts successful runs only.
    /// </summary>
    [Fact]
    public async Task UpdateAfterExecution_OnFailure_AdvancesLastExecutedAt_ButNotExecutionCount()
    {
        var provider = CreateProvider();

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "PollPort",
            Interval = TimeSpan.FromMinutes(1)
        };

        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        var executedAt = DateTime.UtcNow;
        await provider.UpdatePeriodicTickerAfterExecution(ticker.Id, executedAt, succeeded: false);

        var stored = await provider.GetPeriodicTickerById(ticker.Id, default);

        Assert.NotNull(stored);
        Assert.Equal(executedAt, stored.LastExecutedAt);
        Assert.Equal(0, stored.ExecutionCount);
    }

    /// <summary>
    /// Regression for the lost-update race: the in-memory provider advances schedule state with a
    /// compare-and-swap (TryUpdate by reference). Without a retry loop, concurrent advancements race
    /// each other and a losing TryUpdate silently drops the update — LastExecutedAt would stall and
    /// ExecutionCount would undercount. Hammer the method from many threads and assert no increment
    /// is lost.
    /// </summary>
    [Fact]
    public async Task UpdateAfterExecution_UnderConcurrency_DoesNotLoseIncrements()
    {
        var provider = CreateProvider();

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "PollPort",
            Interval = TimeSpan.FromMinutes(1)
        };

        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        const int concurrency = 32;
        const int iterationsPerTask = 50;
        const int expected = concurrency * iterationsPerTask;

        var tasks = new Task[concurrency];
        for (var t = 0; t < concurrency; t++)
        {
            tasks[t] = Task.Run(async () =>
            {
                for (var i = 0; i < iterationsPerTask; i++)
                    await provider.UpdatePeriodicTickerAfterExecution(ticker.Id, DateTime.UtcNow, succeeded: true);
            });
        }

        await Task.WhenAll(tasks);

        var stored = await provider.GetPeriodicTickerById(ticker.Id, default);

        Assert.NotNull(stored);
        Assert.NotNull(stored.LastExecutedAt);
        Assert.Equal(expected, stored.ExecutionCount);
    }
}

