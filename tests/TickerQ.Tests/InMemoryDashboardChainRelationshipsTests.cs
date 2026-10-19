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
    [Fact]
    public async Task Dashboard_metadata_omits_payloads_and_graphs_without_changing_normal_entity_queries()
    {
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(DateTime.UtcNow);
        using var services = new ServiceCollection().AddSingleton(clock).AddSingleton(new SchedulerOptionsBuilder()).BuildServiceProvider();
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        var provider = new TickerInMemoryPersistenceProvider<Job, Cron>(services);
        var root = new Job { Function = "root", Request = new byte[1024 * 1024],
            Children = [new Job { Function = "child", Request = new byte[1024 * 1024],
                Children = [new Job { Function = "leaf", Request = new byte[1024 * 1024] }] }] };
        await provider.AddTimeTickers([root]);
        var rows = await ((ITickerDashboardQueryProvider<Job>)provider).TimeTickersDashboardQuery().ToArrayAsync();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Null(row.Request));
        var marker = Assert.Single(rows.Single(x => x.Id == root.Id).Children);
        Assert.Empty(marker.Children);
        var normal = await provider.TimeTickersQuery().Where(x => x.Id == root.Id).FirstOrDefaultAsync();
        Assert.Equal(1024 * 1024, normal.Request.Length);
        Assert.Single(Assert.Single(normal.Children).Children);
        var service = new TickerDashboardDataService<Job, Cron>(provider);
        var dashboard = await service.GetTimeTickersFlatAsync(new TimeTickerQueryFilter { HasChildren = true });
        Assert.Equal(3, Assert.Single(dashboard.Items).ChainSummary.TotalCount);
        var child = Assert.Single(await service.GetTimeTickerChildrenAsync(root.Id));
        Assert.Equal(1, child.ChildCount);
        Assert.Equal(1, (await service.GetTimeTickerByIdAsync(child.Id)).ChildCount);
    }

}
