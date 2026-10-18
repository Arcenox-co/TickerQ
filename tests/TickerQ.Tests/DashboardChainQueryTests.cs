using NSubstitute;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public sealed class DashboardChainQueryTests
{
    public sealed class Job : TimeTickerEntity<Job> { public Job() { Function = "job"; } }
    public sealed class Cron : CronTickerEntity { }

    [Fact]
    public async Task Wide_chain_reads_by_level_and_sets_child_counts_without_unrelated_rows()
    {
        var root = new Job { Id = Guid.NewGuid(), Function = "root" };
        var jobs = new List<Job> { root, new() { Id = Guid.NewGuid(), Function = "unrelated" } };
        jobs.AddRange(Enumerable.Range(0, 100).Select(_ => new Job { Id = Guid.NewGuid(), ParentId = root.Id, Function = "child" }));
        var provider = Substitute.For<ITickerPersistenceProvider<Job, Cron>>();
        var reads = 0;
        provider.TimeTickersQuery().Returns(_ => new InMemoryTickerQueryable<Job>(ct => { reads++; return Task.FromResult(jobs); }));
        var result = await new TickerDashboardDataService<Job, Cron>(provider).GetTimeTickerChainAsync(root.Id);
        Assert.Equal(101, result.Count); Assert.Equal(3, reads);
        Assert.Equal(100, result.Single(x => x.Id == root.Id).ChildCount);
        Assert.DoesNotContain(result, x => x.FunctionName == "unrelated");
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(501, true)]
    public async Task Chain_node_limit_is_enforced_without_returning_a_partial_tree(int count, bool rejected)
    {
        var root = new Job { Id = Guid.NewGuid() };
        var jobs = new List<Job> { root };
        jobs.AddRange(Enumerable.Range(1, count - 1).Select(_ => new Job { Id = Guid.NewGuid(), ParentId = root.Id }));
        var provider = Substitute.For<ITickerPersistenceProvider<Job, Cron>>();
        provider.TimeTickersQuery().Returns(_ => new InMemoryTickerQueryable<Job>(ct => Task.FromResult(jobs)));
        var service = new TickerDashboardDataService<Job, Cron>(provider);
        if (rejected) await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetTimeTickerChainAsync(root.Id));
        else Assert.Equal(count, (await service.GetTimeTickerChainAsync(root.Id)).Count);
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public async Task Chain_depth_limit_is_enforced(int depth, bool rejected)
    {
        var jobs = new List<Job> { new() { Id = Guid.NewGuid() } };
        for (var i = 0; i < depth; i++) jobs.Add(new Job { Id = Guid.NewGuid(), ParentId = jobs[^1].Id });
        var provider = Substitute.For<ITickerPersistenceProvider<Job, Cron>>();
        provider.TimeTickersQuery().Returns(_ => new InMemoryTickerQueryable<Job>(ct => Task.FromResult(jobs)));
        var service = new TickerDashboardDataService<Job, Cron>(provider);
        if (rejected) await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetTimeTickerChainAsync(jobs[0].Id));
        else Assert.Equal(depth + 1, (await service.GetTimeTickerChainAsync(jobs[0].Id)).Count);
    }
}
