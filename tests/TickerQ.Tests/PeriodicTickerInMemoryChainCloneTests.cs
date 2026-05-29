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

        await provider.UpdatePeriodicTickerAfterExecution(ticker.Id, DateTime.UtcNow, default);

        var stored = await provider.GetPeriodicTickerById(ticker.Id, default);

        Assert.NotNull(stored);
        Assert.Equal(1, stored.ExecutionCount);
        Assert.NotNull(stored.ChainTemplate);
        Assert.Single(stored.ChainTemplate);
        Assert.Equal("ScheduledCalculations", stored.ChainTemplate[0].Function);
        Assert.Equal(ChainOverlapBehavior.Skip, stored.ChainOverlapBehavior);
    }
}

