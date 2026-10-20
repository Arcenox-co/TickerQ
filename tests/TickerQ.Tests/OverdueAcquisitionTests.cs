using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class OverdueAcquisitionTests : OverdueAcquisitionContract<OverdueAcquisitionTests.Job, OverdueAcquisitionTests.Cron>
{
    public sealed class Job : TimeTickerEntity<Job> { }
    public sealed class Cron : CronTickerEntity { }
    protected override ITickerPersistenceProvider<Job, Cron> Provider { get; }
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public OverdueAcquisitionTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        Provider = new TickerInMemoryPersistenceProvider<Job, Cron>(new ServiceCollection()
            .AddSingleton(clock).AddSingleton(new SchedulerOptionsBuilder
                { FallbackBatchSize = 2, AcquisitionBatchSize = 2 }).BuildServiceProvider());
    }

    [Fact]
    public async Task OrdinaryScheduling_DrainsOldBacklogInBoundedBatches_WithoutFallback()
    {
        var due = Now.AddMinutes(-5);
        var jobs = Enumerable.Range(0, 5).Select(i => new Job
        {
            Id = Guid.NewGuid(), Function = "Backlog", ExecutionTime = due.AddMilliseconds(i)
        }).ToArray();
        await Provider.AddTimeTickers(jobs);
        var acquired = new List<Guid>();
        foreach (var expected in new[] { 2, 2, 1 })
        {
            var candidates = await Provider.GetEarliestTimeTickers();
            Assert.Equal(expected, candidates.Length);
            var leases = new List<AcquisitionLease>();
            await foreach (var row in Provider.QueueTimeTickers(candidates))
            {
                acquired.Add(row.Id);
                leases.Add(new AcquisitionLease(row.Id, row.AcquisitionToken!.Value));
            }
            Assert.Equal(expected, (await Provider.TransitionQueuedTimeTickersToInProgressAsync(leases)).Length);
        }
        Assert.Equal(jobs.Select(x => x.Id), acquired);
        Assert.Empty(await Provider.GetEarliestTimeTickers());
    }
}
