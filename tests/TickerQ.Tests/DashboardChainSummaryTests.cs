using NSubstitute;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public sealed class DashboardChainSummaryTests
{
    public sealed class Job : TimeTickerEntity<Job> { public Job() { Function = "job"; } }
    public sealed class Cron : CronTickerEntity { }

    [Theory]
    [InlineData(TickerStatus.InProgress, TickerStatus.Idle, ChainExecutionStatus.Running, 1)]
    [InlineData(TickerStatus.Done, TickerStatus.Queued, ChainExecutionStatus.Waiting, 2)]
    [InlineData(TickerStatus.Failed, TickerStatus.InProgress, ChainExecutionStatus.Running, 2)]
    [InlineData(TickerStatus.Failed, TickerStatus.Idle, ChainExecutionStatus.Waiting, 2)]
    [InlineData(TickerStatus.Done, TickerStatus.DueDone, ChainExecutionStatus.Completed, 3)]
    [InlineData(TickerStatus.Done, TickerStatus.Skipped, ChainExecutionStatus.Completed, 3)]
    [InlineData(TickerStatus.Failed, TickerStatus.Skipped, ChainExecutionStatus.Failed, 3)]
    [InlineData(TickerStatus.Cancelled, TickerStatus.Skipped, ChainExecutionStatus.Cancelled, 3)]
    public async Task Completed_root_uses_all_descendants_without_overwriting_job_status(
        TickerStatus childStatus, TickerStatus grandchildStatus, ChainExecutionStatus expected, int finished)
    {
        var root = new Job { Id = Guid.NewGuid(), Status = TickerStatus.Done, ExecutedAt = DateTime.UtcNow };
        var child = new Job { Id = Guid.NewGuid(), ParentId = root.Id, Status = childStatus };
        var grandchild = new Job { Id = Guid.NewGuid(), ParentId = child.Id, Status = grandchildStatus };
        var unrelated = new Job { Id = Guid.NewGuid(), Status = TickerStatus.Failed, ExecutedAt = DateTime.UtcNow };
        var rows = new List<Job> { root, child, grandchild, unrelated };
        var provider = Provider(rows);
        var service = new TickerDashboardDataService<Job, Cron>(provider);

        var table = (await service.GetTimeTickersFlatAsync(new TimeTickerQueryFilter())).Items.Single(x => x.Id == root.Id);
        var execution = (await service.GetExecutionsFlatAsync(new ExecutionQueryFilter())).Items.Single(x => x.Id == root.Id);
        var recent = (await service.GetRecentActivityAsync(10)).Single(x => x.Id == root.Id);
        foreach (var summary in new[] { table.ChainSummary, execution.ChainSummary, recent.ChainSummary })
        {
            Assert.NotNull(summary);
            Assert.Equal(expected, summary.Status);
            Assert.Equal(3, summary.TotalCount);
            Assert.Equal(finished, summary.FinishedCount);
            Assert.Equal(childStatus == TickerStatus.Failed ? 1 : 0, summary.FailedCount);
            Assert.Equal(grandchildStatus == TickerStatus.Skipped ? 1 : 0, summary.SkippedCount);
        }
        Assert.Equal(TickerStatus.Done, table.Status);
        Assert.Equal(TickerStatus.Done, execution.Status);
        Assert.Equal(TickerStatus.Done, root.Status);
        Assert.Null((await service.GetTimeTickersFlatAsync(new TimeTickerQueryFilter())).Items.Single(x => x.Id == unrelated.Id).ChainSummary);

        // Retrying a failed nested step makes the same chain active again.
        child.Status = TickerStatus.InProgress;
        var retry = (await service.GetTimeTickersFlatAsync(new TimeTickerQueryFilter())).Items.Single(x => x.Id == root.Id).ChainSummary;
        Assert.Equal(ChainExecutionStatus.Running, retry.Status);
        Assert.Equal(0, retry.FailedCount);
    }

    [Fact]
    public async Task Embedded_children_and_separate_rows_are_deduplicated_and_batched_per_level()
    {
        var roots = Enumerable.Range(0, 5).Select(_ => new Job { Id = Guid.NewGuid(), Status = TickerStatus.Done }).ToArray();
        foreach (var root in roots)
            root.Children = new[] { new Job { Id = Guid.NewGuid(), ParentId = root.Id, Status = TickerStatus.InProgress } };
        var reads = 0;
        var provider = Provider(roots.Concat(roots.SelectMany(x => x.Children)).ToList(), () => reads++);
        var page = await new TickerDashboardDataService<Job, Cron>(provider).GetTimeTickersFlatAsync(new TimeTickerQueryFilter());
        Assert.Equal(4, reads); // page, roots, children, empty next level (independent of row count)
        Assert.All(page.Items, x => { Assert.Equal(2, x.ChainSummary.TotalCount); Assert.Equal(ChainExecutionStatus.Running, x.ChainSummary.Status); });

        var embeddedOnly = await new TickerDashboardDataService<Job, Cron>(Provider(roots.ToList()))
            .GetTimeTickersFlatAsync(new TimeTickerQueryFilter());
        Assert.All(embeddedOnly.Items, x => Assert.Equal(2, x.ChainSummary.TotalCount));
    }

    [Fact]
    public async Task Oversized_tree_does_not_report_a_partial_chain_as_completed()
    {
        var root = new Job { Id = Guid.NewGuid(), Status = TickerStatus.Done };
        var rows = new List<Job> { root };
        rows.AddRange(Enumerable.Range(0, 500).Select(_ => new Job { Id = Guid.NewGuid(), ParentId = root.Id, Status = TickerStatus.Done }));
        var result = await new TickerDashboardDataService<Job, Cron>(Provider(rows)).GetTimeTickersFlatAsync(new TimeTickerQueryFilter());
        Assert.Equal(ChainExecutionStatus.Unavailable, Assert.Single(result.Items).ChainSummary.Status);
    }

    private static ITickerPersistenceProvider<Job, Cron> Provider(List<Job> rows, Action read = null)
    {
        var provider = Substitute.For<ITickerPersistenceProvider<Job, Cron>>();
        provider.TimeTickersQuery().Returns(_ => new InMemoryTickerQueryable<Job>(_ => { read?.Invoke(); return Task.FromResult(rows); }));
        provider.CronTickerOccurrencesQuery().Returns(_ => new InMemoryTickerQueryable<CronTickerOccurrenceEntity<Cron>>(
            _ => Task.FromResult(new List<CronTickerOccurrenceEntity<Cron>>())));
        return provider;
    }
}
