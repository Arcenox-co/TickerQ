using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public partial class TickerManagerTests
{
    private TimeTickerEntity ReviewJob() => new() { Function = ValidFunctionName, ExecutionTime = FixedUtcNow.AddDays(1) };
    private CronTickerEntity ReviewCron() => new() { Function = ValidFunctionName, Expression = ValidCronExpression };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Zero_row_inserts_fail_without_notifications_or_scheduler_wake(bool cron, bool batch)
    {
        if (cron)
        {
            if (batch) Assert.False((await _cronTickerManager.AddBatchAsync([ReviewCron()])).IsSucceeded);
            else Assert.False((await _cronTickerManager.AddAsync(ReviewCron())).IsSucceeded);
        }
        else
        {
            if (batch) Assert.False((await _timeTickerManager.AddBatchAsync([ReviewJob()])).IsSucceeded);
            else Assert.False((await _timeTickerManager.AddAsync(ReviewJob())).IsSucceeded);
        }
        Assert.Empty(_notificationHubSender.ReceivedCalls());
        Assert.Empty(_hostScheduler.ReceivedCalls());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Notification_failure_does_not_fail_a_committed_insert_or_prevent_wake(bool cron, bool batch)
    {
        _persistenceProvider.AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistenceProvider.InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        Task Fail() => Task.FromException(new InvalidOperationException("SignalR transport unavailable"));
        _notificationHubSender.AddTimeTickerNotifyAsync(Arg.Any<Guid>()).Returns(_ => Fail());
        _notificationHubSender.AddTimeTickersBatchNotifyAsync().Returns(_ => Fail());
        _notificationHubSender.AddCronTickerNotifyAsync(Arg.Any<Guid>()).Returns(_ => Fail());
        if (cron)
        {
            if (batch) AssertCommitted(await _cronTickerManager.AddBatchAsync([ReviewCron()]));
            else AssertCommitted(await _cronTickerManager.AddAsync(ReviewCron()));
        }
        else
        {
            if (batch) AssertCommitted(await _timeTickerManager.AddBatchAsync([ReviewJob()]));
            else AssertCommitted(await _timeTickerManager.AddAsync(ReviewJob()));
        }
        _hostScheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime?>());
    }

    private static void AssertCommitted<T>(TickerResult<T> result) where T : class
    {
        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        Assert.Null(result.Exception);
        Assert.Equal(1, result.AffectedRows);
    }

    [Fact]
    public async Task Invalid_cron_batch_does_not_change_a_cached_execution_before_persistence()
    {
        var cron = ReviewCron();
        var pending = new InternalFunctionContext { ParentId = cron.Id, ExecutionTime = FixedUtcNow.AddMinutes(10) };
        pending.SetProperty(x => x.Status, TickerStatus.Queued);
        _executionContext.Functions = [pending];
        var invalid = ReviewCron(); invalid.Expression = InvalidCronExpression;

        Assert.False((await _cronTickerManager.UpdateBatchAsync([cron, invalid])).IsSucceeded);

        Assert.Equal(FixedUtcNow.AddMinutes(10), pending.ExecutionTime);
        Assert.Equal([nameof(InternalFunctionContext.Status)], pending.GetPropsToUpdate());
        await _persistenceProvider.DidNotReceive().UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>());
        Assert.Empty(_notificationHubSender.ReceivedCalls());
    }

    [Fact]
    public async Task Caller_cancellation_after_insert_does_not_cancel_immediate_dispatch()
    {
        using var caller = new CancellationTokenSource();
        var job = ReviewJob(); job.ExecutionTime = FixedUtcNow;
        _dispatcher.IsEnabled.Returns(true);
        _persistenceProvider.AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), caller.Token).Returns(_ => { caller.Cancel(); return 1; });
        _persistenceProvider.AcquireImmediateTimeTickersAsync(Arg.Any<Guid[]>(), CancellationToken.None).Returns([job]);

        AssertCommitted(await _timeTickerManager.AddAsync(job, caller.Token));

        await _dispatcher.Received(1).DispatchAsync(Arg.Any<InternalFunctionContext[]>(), CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_failure_after_run_now_is_accepted_and_still_notifies(bool cron)
    {
        _dispatcher.IsEnabled.Returns(true);
        _dispatcher.DispatchAsync(Arg.Any<InternalFunctionContext[]>(), CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("dispatch failed")));
        if (cron)
        {
            var definition = ReviewCron();
            _persistenceProvider.GetCronTickerById(definition.Id, Arg.Any<CancellationToken>()).Returns(definition);
            _persistenceProvider.InsertCronTickerOccurrences(Arg.Any<CronTickerOccurrenceEntity<CronTickerEntity>[]>(), Arg.Any<CancellationToken>()).Returns(1);
            _persistenceProvider.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), CancellationToken.None).Returns(
                [new CronTickerOccurrenceEntity<CronTickerEntity> { CronTicker = definition, CronTickerId = definition.Id }]);
            AssertCommitted(await _cronTickerManager.RunNowAsync(definition.Id));
            await _notificationHubSender.Received(1).AddCronOccurrenceAsync(definition.Id, Arg.Any<Guid>());
        }
        else
        {
            var job = ReviewJob();
            _persistenceProvider.GetTimeTickerById(job.Id, Arg.Any<CancellationToken>()).Returns(job);
            _persistenceProvider.AcquireTimeTickerOnDemandAsync(job.Id, FixedUtcNow, Arg.Any<CancellationToken>()).Returns(job);
            AssertCommitted(await _timeTickerManager.RunNowAsync(job.Id));
            await _notificationHubSender.Received(1).UpdateTimeTickerNotifyAsync(job.Id);
        }
        await _dispatcher.Received(1).DispatchAsync(Arg.Any<InternalFunctionContext[]>(), CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_failure_after_insert_does_not_invite_duplicate_retry()
    {
        _persistenceProvider.InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _hostScheduler.When(x => x.RestartIfNeeded(Arg.Any<DateTime?>())).Do(_ => throw new InvalidOperationException("host stopped"));
        AssertCommitted(await _cronTickerManager.AddAsync(ReviewCron()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Updates_and_deletes_publish_change_hints_for_single_and_batch(bool batch, bool notificationFails)
    {
        var job = ReviewJob(); var cron = ReviewCron();
        if (notificationFails)
        {
            Task Fail() => Task.FromException(new InvalidOperationException("notification failure"));
            _notificationHubSender.UpdateTimeTickerNotifyAsync(Arg.Any<Guid>()).Returns(_ => Fail());
            _notificationHubSender.UpdateCronTickerNotifyAsync(Arg.Any<Guid>()).Returns(_ => Fail());
            _notificationHubSender.RemoveTimeTickerNotifyAsync(Arg.Any<Guid>()).Returns(_ => Fail());
            _notificationHubSender.RemoveCronTickerNotifyAsync(Arg.Any<Guid>()).Returns(_ => Fail());
        }
        _persistenceProvider.UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistenceProvider.UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistenceProvider.RemoveTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistenceProvider.RemoveCronTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(1);
        if (batch)
        {
            AssertCommitted(await _timeTickerManager.UpdateBatchAsync([job]));
            AssertCommitted(await _cronTickerManager.UpdateBatchAsync([cron]));
            AssertCommitted(await _timeTickerManager.DeleteBatchAsync([job.Id]));
            AssertCommitted(await _cronTickerManager.DeleteBatchAsync([cron.Id]));
        }
        else
        {
            AssertCommitted(await _timeTickerManager.UpdateAsync(job));
            AssertCommitted(await _cronTickerManager.UpdateAsync(cron));
            AssertCommitted(await _timeTickerManager.DeleteAsync(job.Id));
            AssertCommitted(await _cronTickerManager.DeleteAsync(cron.Id));
        }
        await _notificationHubSender.Received(1).UpdateTimeTickerNotifyAsync(job.Id);
        await _notificationHubSender.Received(1).UpdateCronTickerNotifyAsync(cron.Id);
        await _notificationHubSender.Received(1).RemoveTimeTickerNotifyAsync(job.Id);
        await _notificationHubSender.Received(1).RemoveCronTickerNotifyAsync(cron.Id);
    }

    [Fact]
    public async Task Missing_rows_do_not_publish_change_hints_or_wake_scheduler()
    {
        await _timeTickerManager.UpdateAsync(ReviewJob()); await _cronTickerManager.UpdateAsync(ReviewCron());
        await _timeTickerManager.DeleteAsync(Guid.NewGuid()); await _cronTickerManager.DeleteAsync(Guid.NewGuid());
        await _timeTickerManager.UpdateBatchAsync([ReviewJob()]); await _cronTickerManager.UpdateBatchAsync([ReviewCron()]);
        await _timeTickerManager.DeleteBatchAsync([Guid.NewGuid()]); await _cronTickerManager.DeleteBatchAsync([Guid.NewGuid()]);
        Assert.Empty(_notificationHubSender.ReceivedCalls()); Assert.Empty(_hostScheduler.ReceivedCalls());
    }

    [Fact]
    public async Task Null_inputs_return_validation_failures_before_persistence()
    {
        static void Invalid<T>(TickerResult<T> result) where T : class { Assert.False(result.IsSucceeded); Assert.IsType<TickerValidatorException>(result.Exception); }
        Invalid(await _timeTickerManager.AddAsync(null!)); Invalid(await _cronTickerManager.AddAsync(null!));
        Invalid(await _timeTickerManager.AddOnceAsync("test", null!));
        Invalid(await _timeTickerManager.AddBatchAsync(null!)); Invalid(await _cronTickerManager.AddBatchAsync(null!));
        Invalid(await _timeTickerManager.AddBatchAsync([null!])); Invalid(await _cronTickerManager.AddBatchAsync([null!]));
        Invalid(await _timeTickerManager.UpdateBatchAsync(null!)); Invalid(await _cronTickerManager.UpdateBatchAsync(null!));
        Invalid(await _timeTickerManager.DeleteBatchAsync(null!)); Invalid(await _cronTickerManager.DeleteBatchAsync(null!));
        var root = ReviewJob(); root.Children.Add(null!); Invalid(await _timeTickerManager.AddAsync(root));
        Assert.Empty(_persistenceProvider.ReceivedCalls());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetEnabled_preserves_definition_and_does_not_mutate_live_source_when_rejected(bool accepted)
    {
        var cron = ReviewCron(); cron.SkipIfAlreadyRunning = true; cron.Retries = 7; cron.Description = "keep";
        _persistenceProvider.GetCronTickerById(cron.Id, Arg.Any<CancellationToken>()).Returns(cron);
        _persistenceProvider.UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(accepted ? 1 : 0);
        var result = await _cronTickerManager.SetEnabledAsync(cron.Id, false);
        Assert.Equal(accepted, result.IsSucceeded);
        Assert.True(cron.IsEnabled);
        await _persistenceProvider.Received(1).UpdateCronTickers(Arg.Is<CronTickerEntity[]>(x =>
            x.Length == 1 && !x[0].IsEnabled && x[0].SkipIfAlreadyRunning && x[0].Retries == 7 && x[0].Description == "keep"), Arg.Any<CancellationToken>());
        if (accepted) { Assert.False(result.Result.IsEnabled); _hostScheduler.Received(1).Restart(); await _notificationHubSender.Received(1).UpdateCronTickerNotifyAsync(cron.Id); }
        else Assert.Empty(_notificationHubSender.ReceivedCalls());
    }

    private sealed class ReviewTypedJob : ITickerFunction
    { public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class ReviewTypedRequestJob : ITickerFunction<ReviewRequest>
    { public Task ExecuteAsync(TickerFunctionContext<ReviewRequest> context, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed record ReviewRequest([property: System.Text.Json.Serialization.JsonPropertyName("OrderId")] string OrderId);

    [Fact]
    public async Task Typed_cron_overloads_resolve_registered_name_and_validate_request()
    {
        TickerFunctionProvider.RegisterTypeMapping(typeof(ReviewTypedJob), ValidFunctionName);
        TickerFunctionProvider.RegisterTypeMapping(typeof(ReviewTypedRequestJob), TypedFunctionName);
        _persistenceProvider.InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        var noRequest = await _cronTickerManager.AddAsync<ReviewTypedJob>(ValidCronExpression);
        AssertCommitted(noRequest); Assert.Equal(ValidFunctionName, noRequest.Result.Function);
        var valid = await _cronTickerManager.AddAsync<ReviewTypedRequestJob, ReviewRequest>(ValidCronExpression, new("order-42"));
        AssertCommitted(valid); Assert.Equal(TypedFunctionName, valid.Result.Function); Assert.Equal(7, valid.Result.RequestContractVersion);
        Assert.False((await _cronTickerManager.AddAsync<ReviewTypedRequestJob, ReviewRequest>(ValidCronExpression, new(""))).IsSucceeded);
        await _persistenceProvider.Received(2).InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Time_run_now_preserves_partition_chain_and_acquisition_metadata()
    {
        var job = ReviewJob();
        var partition = new TickerQRuntimePartition("manager-operations"); _executionContext.RuntimePartition = partition;
        _dispatcher.IsEnabled.Returns(true);
        _persistenceProvider.GetTimeTickerById(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        var token = Guid.NewGuid(); var generation = Guid.NewGuid();
        var acquired = new TimeTickerEntity
        {
            Id = job.Id,
            Function = ValidFunctionName,
            Status = TickerStatus.InProgress,
            AcquisitionToken = token,
            ChainRootId = job.Id,
            ChainGeneration = generation,
            TimeoutSeconds = 42,
            Children = [new() { Id = Guid.NewGuid(), Function = ValidFunctionName, ParentId = job.Id, ChainRootId = job.Id, ChainGeneration = generation }]
        };
        _persistenceProvider.AcquireTimeTickerOnDemandAsync(job.Id, FixedUtcNow, Arg.Any<CancellationToken>()).Returns(acquired);
        AssertCommitted(await _timeTickerManager.RunNowAsync(job.Id));
        await _dispatcher.Received(1).DispatchAsync(Arg.Is<InternalFunctionContext[]>(x =>
            x.Length == 1 && x[0].AcquisitionToken == token && x[0].ChainGeneration == generation &&
            x[0].RuntimePartitionKey == partition.StorageKey && x[0].TimeoutSeconds == 42 &&
            x[0].TimeTickerChildren[0].RuntimePartitionKey == partition.StorageKey && x[0].CachedDelegate != null), CancellationToken.None);
    }

    [Fact]
    public async Task Time_run_now_rejects_stopped_host_missing_jobs_children_and_lost_acquisition()
    {
        Assert.False((await _timeTickerManager.RunNowAsync(Guid.NewGuid())).IsSucceeded);
        Assert.Empty(_persistenceProvider.ReceivedCalls());
        _dispatcher.IsEnabled.Returns(true);
        _persistenceProvider.GetTimeTickerById(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TimeTickerEntity)null);
        _persistenceProvider.AcquireTimeTickerOnDemandAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((TimeTickerEntity)null);
        Assert.False((await _timeTickerManager.RunNowAsync(Guid.NewGuid())).IsSucceeded);
        var job = ReviewJob(); job.ParentId = Guid.NewGuid();
        _persistenceProvider.GetTimeTickerById(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        Assert.False((await _timeTickerManager.RunNowAsync(job.Id)).IsSucceeded);
        await _persistenceProvider.DidNotReceive().AcquireTimeTickerOnDemandAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        job.ParentId = null;
        Assert.False((await _timeTickerManager.RunNowAsync(job.Id)).IsSucceeded);
        await _dispatcher.DidNotReceive().DispatchAsync(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cron_run_now_returns_new_occurrence_and_preserves_overlap_policy()
    {
        var cron = ReviewCron(); cron.SkipIfAlreadyRunning = true; cron.DefinitionRevision = 9;
        _dispatcher.IsEnabled.Returns(true);
        _persistenceProvider.GetCronTickerById(cron.Id, Arg.Any<CancellationToken>()).Returns(cron);
        _persistenceProvider.InsertCronTickerOccurrences(Arg.Any<CronTickerOccurrenceEntity<CronTickerEntity>[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistenceProvider.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns(call =>
            [new CronTickerOccurrenceEntity<CronTickerEntity> { Id = call.ArgAt<Guid[]>(0)[0], CronTickerId = cron.Id, CronTicker = cron, Status = TickerStatus.InProgress, AcquisitionToken = Guid.NewGuid() }]);
        _notificationHubSender.AddCronOccurrenceAsync(cron.Id, Arg.Any<Guid>()).Returns(Task.FromException(new Exception("notification failure")));
        var result = await _cronTickerManager.RunNowAsync(cron.Id);
        AssertCommitted(result); Assert.NotEqual(cron.Id, result.Result.Id); Assert.Equal(cron.Id, result.Result.CronTickerId);
        Assert.Equal(FixedUtcNow, result.Result.ExecutionTime); Assert.Equal(9, result.Result.DefinitionRevision);
        await _dispatcher.Received(1).DispatchAsync(Arg.Is<InternalFunctionContext[]>(x => x[0].SkipIfAlreadyRunning && x[0].TickerId == result.Result.Id), CancellationToken.None);
    }
}
