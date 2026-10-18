using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Enums;

namespace TickerQ.Tests;

public sealed class ChainDeleteBehaviorTests : ChainDeleteBehaviorContract<ChainDeleteBehaviorTests.Job, ChainDeleteBehaviorTests.Cron>
{
    public sealed class Job : TimeTickerEntity<Job> { }
    public sealed class Cron : CronTickerEntity { }
    protected override ITickerPersistenceProvider<Job, Cron> Provider { get; }
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public ChainDeleteBehaviorTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        Provider = new TickerInMemoryPersistenceProvider<Job, Cron>(new ServiceCollection()
            .AddSingleton(clock).AddSingleton(new SchedulerOptionsBuilder()).BuildServiceProvider());
    }

    [Fact]
    public async Task Restrict_blocks_replacement_until_descendants_finish_then_retention_resumes()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var replacement = Node();
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), TickerStatus.Idle);
        await Provider.AddTimeTickers([root]);
        Assert.Equal(0, await Provider.ReplaceTimeTickerChainAsync(root.Id, replacement));
        Assert.Null(await Provider.GetTimeTickerById(replacement.Id));
        var finished = await Provider.GetTimeTickerById(root.Id);
        await SetDescendantStatusAsync(root.Id, Assert.Single(Assert.Single(finished.Children).Children).Id, TickerStatus.Done);
        Assert.Equal(ChainDeleteBehavior.Restrict, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
        Assert.Equal(3, (await Provider.DeleteEligibleTimeTickerChainsAsync(new(Now.AddDays(-7), null, null, null), 10, default)).Deleted);
    }

    [Fact]
    public async Task Restrict_allows_replacement_when_all_descendants_are_terminal()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var replacement = Node();
        await Provider.AddTimeTickers([root]);
        Assert.True(await Provider.ReplaceTimeTickerChainAsync(root.Id, replacement) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
        Assert.NotNull(await Provider.GetTimeTickerById(replacement.Id));
    }
}
