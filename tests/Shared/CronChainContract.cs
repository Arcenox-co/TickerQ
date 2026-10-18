using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests.Shared;

public abstract class CronChainContract<TTime, TCron>
    where TTime : TimeTickerEntity<TTime>, new()
    where TCron : CronTickerEntity, new()
{
    protected abstract ITickerPersistenceProvider<TTime, TCron> Provider { get; }
    protected abstract DateTime Now { get; }
    private int _sequence;
    private async Task<TCron> Schedule(bool skipIfAlreadyRunning = false)
    {
        var cron = new TCron {
            Id = Guid.NewGuid(), Function = "Prepare", Expression = "* * * * *", Retries = 2,
            Request = new byte[] { 1, 2 }, RetryIntervals = new[] { 2, 5 }, TimeoutSeconds = 12,
            SkipIfAlreadyRunning = skipIfAlreadyRunning,
            Chain = new CronChainTemplate { Children = new() {
                new CronChainNode { Function = "Send", RunCondition = RunCondition.OnSuccess,
                    Request = new byte[] { 3 }, RequestContractVersion = 3, RequestContractFingerprint = "test-contract",
                    Children = new() { new CronChainNode { Function = "Audit", RunCondition = RunCondition.OnAnyCompletedStatus } } }
            } }
        };
        await Provider.InsertCronTickers(new[] { cron }, CancellationToken.None);
        return await Provider.GetCronTickerById(cron.Id, CancellationToken.None);
    }
    [Fact]
    public async Task Cron_policy_survives_definition_queries_queueing_and_manual_acquisition()
    {
        var cron = await Schedule(skipIfAlreadyRunning: true);
        Assert.True((await Provider.GetCronTickerById(cron.Id, default)).SkipIfAlreadyRunning);
        var projected = Assert.Single(await Provider.GetAllCronTickerExpressions(default), x => x.Id == cron.Id);
        Assert.True(projected.SkipIfAlreadyRunning);
        Assert.Equal(cron.DefinitionRevision, projected.DefinitionRevision);
        Assert.Equal(cron.TimeoutSeconds, projected.TimeoutSeconds);
        var item = new InternalManagerContext(cron.Id) {
            FunctionName = projected.Function, Expression = projected.Expression, DefinitionRevision = projected.DefinitionRevision,
            TimeoutSeconds = projected.TimeoutSeconds,
            SkipIfAlreadyRunning = projected.SkipIfAlreadyRunning
        };
        var queuedCount = 0;
        await foreach (var queued in Provider.QueueCronTickerOccurrences((Now.AddSeconds(10), new[] { item })))
        {
            queuedCount++;
            Assert.True(queued.CronTicker.SkipIfAlreadyRunning);
            Assert.Equal(cron.DefinitionRevision, queued.DefinitionRevision);
            Assert.Equal(cron.DefinitionRevision, queued.CronTicker.DefinitionRevision);
        }
        Assert.Equal(1, queuedCount);
        var execution = await Acquire(cron);
        var stored = Assert.Single(await Provider.GetAllCronTickerOccurrences(x => x.Id == execution.TickerId));
        // The returned acquisition snapshot is exercised by Acquire itself.
        Assert.Equal(cron.Id, stored.CronTickerId);
        cron.SkipIfAlreadyRunning = false;
        await Provider.UpdateCronTickers(new[] { cron }, default);
        Assert.False((await Provider.GetCronTickerById(cron.Id, default)).SkipIfAlreadyRunning);
    }

    private async Task<InternalFunctionContext> Acquire(TCron cron)
    {
        var occurrence = new CronTickerOccurrenceEntity<TCron> {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = cron.DefinitionRevision,
            ExecutionTime = Now.AddTicks(++_sequence * TimeSpan.TicksPerSecond), CreatedAt = Now, UpdatedAt = Now
        };
        await Provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        var acquired = Assert.Single(await Provider.AcquireImmediateCronOccurrencesAsync(new[] { occurrence.Id }));
        Assert.Equal(cron.SkipIfAlreadyRunning, acquired.CronTicker.SkipIfAlreadyRunning);
        return new InternalFunctionContext { TickerId = acquired.Id, ParentId = cron.Id,
            Type = TickerType.CronTickerOccurrence, AcquisitionToken = acquired.AcquisitionToken,
            RuntimePartitionKey = acquired.ApplicationNamespaceKey };
    }
    private async Task<CronTickerOccurrenceEntity<TCron>> Receipt(Guid id) =>
        Assert.Single(await Provider.GetAllCronTickerOccurrences(x => x.Id == id));

    [Fact]
    public async Task Trigger_atomically_creates_a_fresh_snapshot_and_receipt()
    {
        var cron = await Schedule();
        var trigger = await Acquire(cron);
        Assert.Equal(CronChainDispatchResult.Created, await Provider.MaterializeCronChainAsync(trigger, false));
        var receipt = await Receipt(trigger.TickerId);
        Assert.Equal(trigger.TickerId, receipt.ChainRootId);
        Assert.Equal(TickerStatus.Done, receipt.Status);
        Assert.Null(receipt.AcquisitionToken);
        Assert.Null(receipt.LockHolder);
        var root = await Provider.GetTimeTickerById(trigger.TickerId);
        Assert.Equal(TickerStatus.Idle, root.Status);
        Assert.Equal(cron.Function, root.Function);
        Assert.Equal(cron.Request, root.Request);
        Assert.Equal(cron.RetryIntervals, root.RetryIntervals);
        Assert.Equal(cron.TimeoutSeconds, root.TimeoutSeconds);
        Assert.Equal(cron.Id, root.OriginCronTickerId);
        Assert.Equal(ChainDeleteBehavior.Restrict, root.DeleteBehavior);
        var child = Assert.Single(root.Children);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(root.Id, child.ChainRootId);
        Assert.Equal(cron.Id, child.OriginCronTickerId);
        Assert.Equal("test-contract", child.RequestContractFingerprint);
        Assert.Equal(RunCondition.OnSuccess, child.RunCondition);
        Assert.Equal(child.Id, Assert.Single(child.Children).ParentId);
        var acquiredRoot = Assert.Single(await Provider.AcquireImmediateTimeTickersAsync(new[] { root.Id }));
        Assert.NotNull(acquiredRoot.AcquisitionToken);
        Assert.Single(Assert.Single(acquiredRoot.Children).Children);
    }

    [Fact]
    public async Task Retrying_a_committed_trigger_returns_the_same_chain()
    {
        var cron = await Schedule(); var trigger = await Acquire(cron);
        Assert.Equal(CronChainDispatchResult.Created, await Provider.MaterializeCronChainAsync(trigger, false));
        var childId = Assert.Single((await Provider.GetTimeTickerById(trigger.TickerId)).Children).Id;
        Assert.Equal(CronChainDispatchResult.AlreadyCreated, await Provider.MaterializeCronChainAsync(trigger, true));
        Assert.Equal(childId, Assert.Single((await Provider.GetTimeTickerById(trigger.TickerId)).Children).Id);
        Assert.Single(await Provider.GetTimeTickers(x => x.ParentId == null));
    }

    [Fact]
    public async Task Occurrence_displays_chain_root_holder_after_trigger_releases_its_lock()
    {
        var cron = await Schedule();
        var trigger = await Acquire(cron);
        Assert.Equal(CronChainDispatchResult.Created, await Provider.MaterializeCronChainAsync(trigger, false));
        var dashboard = new TickerDashboardDataService<TTime, TCron>(Provider);
        Assert.Null((await dashboard.GetCronOccurrenceByIdAsync(trigger.TickerId)).ChainLockHolder);
        var acquired = Assert.Single(await Provider.AcquireImmediateTimeTickersAsync(new[] { trigger.TickerId }));
        var holder = (await Provider.GetTimeTickerById(acquired.Id)).LockHolder;
        Assert.NotNull(holder);

        var detail = await dashboard.GetCronOccurrenceByIdAsync(trigger.TickerId);
        var row = Assert.Single((await dashboard.GetCronOccurrencesFlatAsync(cron.Id, new CronOccurrenceQueryFilter())).Items);
        Assert.Null(detail.LockHolder);
        Assert.Null(row.LockHolder);
        Assert.Equal(holder, detail.ChainLockHolder);
        Assert.Equal(holder, row.ChainLockHolder);
        Assert.Null((await Receipt(trigger.TickerId)).LockHolder);
    }

    [Fact]
    public async Task Time_ticker_table_excludes_cron_chains_before_pagination()
    {
        var cron = await Schedule();
        var trigger = await Acquire(cron);
        Assert.Equal(CronChainDispatchResult.Created, await Provider.MaterializeCronChainAsync(trigger, false));
        // Use the same handler as the cron: origin, not function name, determines visibility.
        var chain = new TTime { Function = cron.Function, ExecutionTime = Now.AddMinutes(1),
            Children = new[] { new TTime { Function = "Send" } } };
        var standalone = new TTime { Function = cron.Function, ExecutionTime = Now.AddMinutes(2) };
        await Provider.AddTimeTickers(new[] { chain, standalone });
        var expected = new[] { chain, standalone };
        var dashboard = new TickerDashboardDataService<TTime, TCron>(Provider);
        var filter = new TimeTickerQueryFilter { FunctionName = cron.Function,
            Search = cron.Function, Statuses = new[] { TickerStatus.Idle }, ScheduledFrom = Now.AddDays(-1),
            ScheduledTo = Now.AddDays(1), SortBy = "scheduledFor", SortDescending = false, PageSize = 1 };
        for (var page = 1; page <= expected.Length + 1; page++)
        {
            filter.PageNumber = page;
            var result = await dashboard.GetTimeTickersFlatAsync(filter);
            Assert.Equal(expected.Length, result.TotalCount);
            if (page <= expected.Length)
            {
                var item = Assert.Single(result.Items);
                Assert.Equal(expected[page - 1].Id, item.Id);
                Assert.Null(item.OriginCronTickerId);
            }
            else Assert.Empty(result.Items);
        }

        // The table filter must not delete or hide the stored chain from direct lookups.
        var root = await Provider.GetTimeTickerById(trigger.TickerId);
        Assert.Equal(cron.Id, root.OriginCronTickerId);
        var child = Assert.Single(root.Children);
        Assert.Equal(cron.Id, child.OriginCronTickerId);
        Assert.Equal(cron.Id, Assert.Single(child.Children).OriginCronTickerId);
        var occurrence = await dashboard.GetCronOccurrenceByIdAsync(trigger.TickerId);
        Assert.Equal(trigger.TickerId, occurrence.ChainRootId);
        var history = await dashboard.GetExecutionsFlatAsync(new ExecutionQueryFilter { Types = new[] { ExecutionType.CronOccurrence } });
        Assert.Equal(trigger.TickerId, Assert.Single(history.Items).ChainRootId);
    }

    [Fact]
    public async Task Stale_owner_cannot_publish_or_complete_the_occurrence()
    {
        var trigger = await Acquire(await Schedule()); var token = trigger.AcquisitionToken;
        trigger.AcquisitionToken = Guid.NewGuid();
        Assert.Equal(CronChainDispatchResult.LostOwnership, await Provider.MaterializeCronChainAsync(trigger, false));
        Assert.Null(await Provider.GetTimeTickerById(trigger.TickerId));
        Assert.Equal(token, (await Receipt(trigger.TickerId)).AcquisitionToken);
    }

    [Fact]
    public async Task Allow_overlap_creates_distinct_trees_for_distinct_occurrences()
    {
        var cron = await Schedule(); var first = await Acquire(cron); var second = await Acquire(cron);
        await Provider.MaterializeCronChainAsync(first, false);
        Assert.Equal(CronChainDispatchResult.Created, await Provider.MaterializeCronChainAsync(second, false));
        var firstRoot = await Provider.GetTimeTickerById(first.TickerId);
        var secondRoot = await Provider.GetTimeTickerById(second.TickerId);
        Assert.NotEqual(firstRoot.Id, secondRoot.Id);
        Assert.NotEqual(Assert.Single(firstRoot.Children).Id, Assert.Single(secondRoot.Children).Id);
    }

    [Fact]
    public async Task Skip_overlap_does_not_create_another_graph()
    {
        var cron = await Schedule(skipIfAlreadyRunning: true);
        var first = await Acquire(cron); await Provider.MaterializeCronChainAsync(first, false);
        var second = await Acquire(cron);
        Assert.Equal(CronChainDispatchResult.Skipped, await Provider.MaterializeCronChainAsync(second, false));
        Assert.Null(await Provider.GetTimeTickerById(second.TickerId));
        var receipt = await Receipt(second.TickerId);
        Assert.Equal(TickerStatus.Skipped, receipt.Status);
        Assert.Contains("unfinished", receipt.SkippedReason);
        Assert.Null(receipt.ChainRootId);
    }

    [Fact]
    public async Task Concurrent_skip_admission_has_one_winner()
    {
        var cron = await Schedule(skipIfAlreadyRunning: true);
        var first = await Acquire(cron); var second = await Acquire(cron);
        var results = await Task.WhenAll(Provider.MaterializeCronChainAsync(first, false), Provider.MaterializeCronChainAsync(second, false));
        Assert.Single(results.Where(x => x == CronChainDispatchResult.Created));
        Assert.Single(results.Where(x => x == CronChainDispatchResult.Skipped));
        Assert.Single(await Provider.GetTimeTickers(x => x.ParentId == null));
    }

    [Fact]
    public async Task Dashboard_chain_summary_tracks_nested_jobs_after_trigger_and_parent_complete()
    {
        var cron = await Schedule();
        var trigger = await Acquire(cron);
        await Provider.MaterializeCronChainAsync(trigger, false);
        var dashboard = new TickerDashboardDataService<TTime, TCron>(Provider);
        var initial = await dashboard.GetCronOccurrenceByIdAsync(trigger.TickerId);
        Assert.Equal(TickerStatus.Done, initial.Status);
        Assert.Equal(ChainExecutionStatus.Waiting, initial.ChainSummary.Status);
        Assert.Equal(3, initial.ChainSummary.TotalCount);
        Assert.Equal(0, initial.ChainSummary.FinishedCount);

        var root = Assert.Single(await Provider.AcquireImmediateTimeTickersAsync(new[] { trigger.TickerId }));
        var child = Assert.Single(root.Children);
        var grandchild = Assert.Single(child.Children);
        await Finish(root, root.Id, null, TickerStatus.Done, release: false);
        await Finish(root, child.Id, root.Id, TickerStatus.InProgress, release: false);
        var running = await dashboard.GetCronOccurrenceByIdAsync(trigger.TickerId);
        Assert.Equal(ChainExecutionStatus.Running, running.ChainSummary.Status);
        Assert.Equal(1, running.ChainSummary.FinishedCount);
        Assert.Equal(ChainExecutionStatus.Running, (await dashboard.GetCronTickerByIdAsync(cron.Id)).LastRunChainSummary.Status);
        var execution = Assert.Single((await dashboard.GetExecutionsFlatAsync(new ExecutionQueryFilter { Types = new[] { ExecutionType.CronOccurrence } })).Items);
        Assert.Equal(ChainExecutionStatus.Running, execution.ChainSummary.Status);
        Assert.Equal(TickerStatus.Done, execution.Status);
        Assert.Equal(ChainExecutionStatus.Running, (await dashboard.GetRecentActivityAsync(10)).First(x => x.Id == trigger.TickerId).ChainSummary.Status);

        await Finish(root, child.Id, root.Id, TickerStatus.Failed, release: false);
        var waiting = await dashboard.GetCronOccurrenceByIdAsync(trigger.TickerId);
        Assert.Equal(ChainExecutionStatus.Waiting, waiting.ChainSummary.Status);
        Assert.Equal(1, waiting.ChainSummary.FailedCount);
        await Finish(root, grandchild.Id, child.Id, TickerStatus.Skipped);
        var finished = Assert.Single((await dashboard.GetCronOccurrencesFlatAsync(cron.Id, new CronOccurrenceQueryFilter())).Items);
        Assert.Equal(ChainExecutionStatus.Failed, finished.ChainSummary.Status);
        Assert.Equal(3, finished.ChainSummary.FinishedCount);
        Assert.Equal(1, finished.ChainSummary.SkippedCount);
        Assert.Equal(TickerStatus.Done, finished.Status);
        Assert.Equal(TickerStatus.Done, (await Provider.GetTimeTickerById(root.Id)).Status);
    }

    private async Task Finish(TimeTickerEntity root, Guid id, Guid? parentId, TickerStatus status, bool release = true)
    {
        var context = new InternalFunctionContext { TickerId = id, ParentId = parentId, Type = TickerType.TimeTicker,
            RuntimePartitionKey = root.ApplicationNamespaceKey, ChainRootId = root.Id,
            ChainGeneration = root.ChainGeneration, AcquisitionToken = root.AcquisitionToken };
        context.SetProperty(x => x.Status, status).SetProperty(x => x.ExecutedAt, Now);
        if (release) context.SetProperty(x => x.ReleaseLock, true);
        Assert.True(await Provider.UpdateTimeTicker(context) > 0);
    }

    [Fact]
    public async Task Finished_root_still_blocks_overlap_until_all_descendants_are_terminal()
    {
        var cron = await Schedule(skipIfAlreadyRunning: true);
        var first = await Acquire(cron); await Provider.MaterializeCronChainAsync(first, false);
        var root = Assert.Single(await Provider.AcquireImmediateTimeTickersAsync(new[] { first.TickerId }));
        await Finish(root, root.Id, null, TickerStatus.Done, release: false);
        Assert.Equal(CronChainDispatchResult.Skipped, await Provider.MaterializeCronChainAsync(await Acquire(cron), false));
        var child = Assert.Single(root.Children); var grandchild = Assert.Single(child.Children);
        await Finish(root, child.Id, root.Id, TickerStatus.Failed);
        Assert.Equal(CronChainDispatchResult.Skipped, await Provider.MaterializeCronChainAsync(await Acquire(cron), false));
        await Finish(root, grandchild.Id, child.Id, TickerStatus.Skipped);
        Assert.Equal(CronChainDispatchResult.Created, await Provider.MaterializeCronChainAsync(await Acquire(cron), false));
    }

    [Fact]
    public async Task Editing_template_changes_future_runs_but_keeps_existing_snapshot()
    {
        var cron = await Schedule(); var first = await Acquire(cron);
        await Provider.MaterializeCronChainAsync(first, false);
        cron.Chain.Children[0].Function = "Replacement";
        await Provider.UpdateCronTickers(new[] { cron }, CancellationToken.None);
        cron = await Provider.GetCronTickerById(cron.Id, CancellationToken.None);
        var second = await Acquire(cron); await Provider.MaterializeCronChainAsync(second, false);
        Assert.Equal("Send", Assert.Single((await Provider.GetTimeTickerById(first.TickerId)).Children).Function);
        Assert.Equal("Replacement", Assert.Single((await Provider.GetTimeTickerById(second.TickerId)).Children).Function);
    }

    [Fact]
    public async Task Completed_receipt_prevents_recreation_after_chain_cleanup()
    {
        var cron = await Schedule(); var trigger = await Acquire(cron);
        await Provider.MaterializeCronChainAsync(trigger, false);
        var root = Assert.Single(await Provider.AcquireImmediateTimeTickersAsync(new[] { trigger.TickerId }));
        var child = Assert.Single(root.Children); var grandchild = Assert.Single(child.Children);
        await Finish(root, child.Id, root.Id, TickerStatus.Done);
        await Finish(root, grandchild.Id, child.Id, TickerStatus.Done);
        await Finish(root, root.Id, null, TickerStatus.Done);
        Assert.True(await Provider.RemoveTimeTickers(new[] { root.Id }) > 0);
        Assert.Equal(CronChainDispatchResult.AlreadyCreated, await Provider.MaterializeCronChainAsync(trigger, false));
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
    }

    [Fact]
    public async Task Removed_template_does_not_forget_an_existing_launch_receipt()
    {
        var cron = await Schedule(); var trigger = await Acquire(cron);
        await Provider.MaterializeCronChainAsync(trigger, false);
        cron.Chain = null; await Provider.UpdateCronTickers(new[] { cron }, CancellationToken.None);
        Assert.Equal(CronChainDispatchResult.AlreadyCreated, await Provider.MaterializeCronChainAsync(trigger, false));
        Assert.Single(await Provider.GetTimeTickers(x => x.ParentId == null));
    }

    [Fact]
    public async Task Missing_trigger_cannot_fall_back_to_executing_the_root_function()
    {
        var trigger = await Acquire(await Schedule()); trigger.TickerId = Guid.NewGuid();
        Assert.Equal(CronChainDispatchResult.LostOwnership, await Provider.MaterializeCronChainAsync(trigger, false));
        Assert.Null(await Provider.GetTimeTickerById(trigger.TickerId));
    }

    [Fact]
    public async Task Mismatched_runtime_partition_cannot_publish_a_chain()
    {
        var trigger = await Acquire(await Schedule());
        trigger.RuntimePartitionKey = "wrong-runtime-partition";
        Assert.Equal(CronChainDispatchResult.LostOwnership, await Provider.MaterializeCronChainAsync(trigger, false));
        Assert.Null(await Provider.GetTimeTickerById(trigger.TickerId));
        Assert.Equal(TickerStatus.InProgress, (await Receipt(trigger.TickerId)).Status);
    }

    [Fact]
    public async Task Ordinary_cron_preserves_the_existing_function_execution_path()
    {
        var cron = await Schedule(); cron.Chain = null;
        await Provider.UpdateCronTickers(new[] { cron }, CancellationToken.None);
        cron = await Provider.GetCronTickerById(cron.Id, CancellationToken.None);
        var trigger = await Acquire(cron);
        Assert.Equal(CronChainDispatchResult.NotConfigured, await Provider.MaterializeCronChainAsync(trigger, false));
        Assert.Equal(TickerStatus.InProgress, (await Receipt(trigger.TickerId)).Status);
    }
}
