using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using Xunit;

namespace TickerQ.Tests.Shared;

// Each fixture configures FallbackBatchSize = 2 and an empty operational store.
public abstract class OverdueAcquisitionContract<TTime, TCron>
    where TTime : TimeTickerEntity<TTime>, new()
    where TCron : CronTickerEntity, new()
{
    protected abstract ITickerPersistenceProvider<TTime, TCron> Provider { get; }
    protected abstract DateTime Now { get; }

    [Fact]
    public async Task Overdue_time_batches_preserve_chains_and_eventually_acquire_every_root_once()
    {
        var roots = Enumerable.Range(0, 5).Select(i => new TTime
        {
            Id = Guid.NewGuid(), Function = "BatchTest", ExecutionTime = Now.AddMinutes(-10 + i),
            Children = [new TTime { Id = Guid.NewGuid(), Function = "BatchChild" }]
        }).ToArray();
        await Provider.AddTimeTickers(roots);
        var acquired = new List<Guid>();
        foreach (var count in new[] { 2, 2, 1, 0 })
        {
            var batch = new List<TimeTickerEntity>();
            await foreach (var row in Provider.QueueTimedOutTimeTickers()) batch.Add(row);
            Assert.Equal(count, batch.Count);
            foreach (var row in batch)
            {
                Assert.NotNull(row.AcquisitionToken);
                Assert.Single(row.Children);
                acquired.Add(row.Id);
            }
        }
        Assert.Equal(5, acquired.Distinct().Count());
        Assert.Equal(roots.Select(x => x.Id), acquired);
    }

    [Fact]
    public async Task Overdue_cron_batches_eventually_acquire_every_occurrence_once()
    {
        var cron = new TCron { Id = Guid.NewGuid(), Function = "BatchCron", Expression = "* * * * *" };
        await Provider.InsertCronTickers([cron], CancellationToken.None);
        cron = await Provider.GetCronTickerById(cron.Id, CancellationToken.None);
        var occurrences = Enumerable.Range(0, 5).Select(i => new CronTickerOccurrenceEntity<TCron>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id,
            DefinitionRevision = cron.DefinitionRevision,
            ExecutionTime = Now.AddMinutes(-10 + i), Status = TickerStatus.Idle,
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        }).ToArray();
        await Provider.InsertCronTickerOccurrences(occurrences, CancellationToken.None);
        var acquired = new List<Guid>();
        foreach (var count in new[] { 2, 2, 1, 0 })
        {
            var batch = new List<CronTickerOccurrenceEntity<TCron>>();
            await foreach (var row in Provider.QueueTimedOutCronTickerOccurrences()) batch.Add(row);
            Assert.Equal(count, batch.Count);
            Assert.All(batch, row => Assert.NotNull(row.AcquisitionToken));
            acquired.AddRange(batch.Select(x => x.Id));
        }
        Assert.Equal(occurrences.Select(x => x.Id), acquired);
    }
}
