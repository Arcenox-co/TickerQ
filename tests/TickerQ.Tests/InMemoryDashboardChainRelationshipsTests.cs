using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class InMemoryDashboardChainRelationshipsTests
{
    public sealed class Job : TimeTickerEntity<Job> { public string CustomValue { get; set; } = "custom"; }
    public sealed class Cron : CronTickerEntity { }

    [Fact]
    public async Task Finished_chain_remains_a_chain_in_lists_filters_and_execution_history()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(now);
        using var services = new ServiceCollection().AddSingleton(clock).AddSingleton(new SchedulerOptionsBuilder()).BuildServiceProvider();
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        var provider = new TickerInMemoryPersistenceProvider<Job, Cron>(services);
        var root = new Job { Function = "root", ExecutionTime = now, Children = [new Job { Function = "child" }] };
        var standalone = new Job { Function = "standalone", ExecutionTime = now.AddDays(1) };
        var cronId = Guid.NewGuid();
        var cronChain = new Job { Function = "root", ExecutionTime = now, OriginCronTickerId = cronId,
            Children = [new Job { Function = "child", OriginCronTickerId = cronId }] };
        var cronRootWithoutChildren = new Job { Function = "standalone", ExecutionTime = now, OriginCronTickerId = cronId };
        await provider.AddTimeTickers([root, standalone, cronChain, cronRootWithoutChildren]);
        var snapshot = await provider.TimeTickersQuery().WithRelated(TickerRelation.Children).Where(x => x.Id == root.Id).FirstOrDefaultAsync();
        Assert.Equal("custom", snapshot.CustomValue);
        Assert.Single(snapshot.Children);
        snapshot.Children.Clear(); snapshot.Function = "must-not-leak";
        var acquired = Assert.Single(await provider.AcquireImmediateTimeTickersAsync([root.Id]));
        var finish = new InternalFunctionContext { TickerId = acquired.Id, Type = TickerType.TimeTicker,
            RuntimePartitionKey = acquired.ApplicationNamespaceKey, AcquisitionToken = acquired.AcquisitionToken,
            ChainRootId = acquired.ChainRootId, ChainGeneration = acquired.ChainGeneration };
        finish.SetProperty(x => x.Status, TickerStatus.Done).SetProperty(x => x.ExecutedAt, now).SetProperty(x => x.ReleaseLock, true);
        Assert.Equal(1, await provider.UpdateTimeTicker(finish));
        var dashboard = new TickerDashboardDataService<Job, Cron>(provider);
        var chains = await dashboard.GetTimeTickersFlatAsync(new TimeTickerQueryFilter { HasChildren = true });
        var chain = Assert.Single(chains.Items);
        Assert.Equal(root.Id, chain.Id); Assert.Equal("root", chain.FunctionName); Assert.Equal(1, chain.ChildCount);
        var singles = await dashboard.GetTimeTickersFlatAsync(new TimeTickerQueryFilter { HasChildren = false });
        Assert.Equal(standalone.Id, Assert.Single(singles.Items).Id);
        var history = await dashboard.GetExecutionsFlatAsync(new ExecutionQueryFilter { Types = [ExecutionType.TimeTicker] });
        Assert.Equal(1, Assert.Single(history.Items).ChildCount);
        Assert.Single((await provider.GetTimeTickerById(root.Id, default)).Children);
    }
}
