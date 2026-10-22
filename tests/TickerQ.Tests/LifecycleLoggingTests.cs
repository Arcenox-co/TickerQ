using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public sealed class LifecycleLoggingTests
{
    private readonly ITickerClock _clock = Substitute.For<ITickerClock>();
    public LifecycleLoggingTests() => _clock.UtcNow.Returns(_ => DateTime.UtcNow);

    private InternalFunctionContext Context(TickerType type = TickerType.TimeTicker) => new()
    {
        TickerId = Guid.NewGuid(), FunctionName = "LifecycleTest", Type = type,
        RuntimePartitionKey = TickerQRuntimePartition.LegacyGlobal.StorageKey,
        AcquisitionToken = Guid.NewGuid(), ExecutionTime = _clock.UtcNow,
        Retries = 1, RetryIntervals = [0]
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handler_records_retry_and_acknowledged_outcome_in_order(bool fail)
    {
        var options = new ExecutionLogOptions();
        var store = new InMemoryTickerExecutionLogStore(options);
        var writer = new TickerLifecycleLogger(store, options, _clock);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.SupportsResultPublication.Returns(true);
        provider.SupportsAcknowledgedTerminalUpdates.Returns(true);
        provider.CommitSuccessfulTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.CommitTerminalTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(provider, _clock,
            Substitute.For<ITickerQNotificationHubSender>(), new SchedulerOptionsBuilder(), writer);
        using var services = new ServiceCollection().AddSingleton(writer).BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(services, _clock, Substitute.For<ITickerQInstrumentation>(), manager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(_clock), new TickerFunctionConcurrencyGate());
        var context = Context(TickerType.CronTickerOccurrence);
        context.CachedDelegate = (_, _, execution) =>
        {
            if (fail || execution.RetryCount == 0) throw new InvalidOperationException("Temporary failure");
            return Task.CompletedTask;
        };
        writer.Created(context.TickerId, context.FunctionName, context.Type, _clock.UtcNow, context.RuntimePartitionKey);
        writer.Queued(context);
        await handler.ExecuteTaskAsync(context, isDue: false);

        var lines = store.GetTail(context.TickerId);
        Assert.Equal(new[] { TickerLifecycleEvent.Created, TickerLifecycleEvent.Queued, TickerLifecycleEvent.Started,
            TickerLifecycleEvent.RetryScheduled, TickerLifecycleEvent.Started, TickerLifecycleEvent.Completed }, lines.Select(x => x.LifecycleEvent));
        Assert.Equal(new[] { 1, 2 }, lines.Where(x => x.LifecycleEvent == TickerLifecycleEvent.Started).Select(x => x.Attempt));
        Assert.Equal(fail ? TickerStatus.Failed : TickerStatus.Done, lines[^1].Status);
        Assert.Contains("1 retries", lines[^1].Message);
        Assert.DoesNotContain("StackTrace", lines[^1].Message);
        if (fail) Assert.Contains("Temporary failure", lines[^1].Message);
        else Assert.DoesNotContain("Temporary failure", lines[^1].Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handler_logs_only_accepted_progress_with_percent_and_message(bool accepted)
    {
        var options = new ExecutionLogOptions();
        var store = new InMemoryTickerExecutionLogStore(options);
        var writer = new TickerLifecycleLogger(store, options, _clock);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.SupportsResultPublication.Returns(true);
        provider.SupportsAcknowledgedTerminalUpdates.Returns(true);
        provider.UpdateExecutionProgressAsync(Arg.Any<TickerProgressUpdate>(), Arg.Any<CancellationToken>())
            .Returns(accepted);
        provider.CommitSuccessfulTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(provider, _clock,
            Substitute.For<ITickerQNotificationHubSender>(), new SchedulerOptionsBuilder(), writer);
        using var services = new ServiceCollection().AddSingleton(writer).BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(services, _clock, Substitute.For<ITickerQInstrumentation>(), manager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(_clock),
            new TickerFunctionConcurrencyGate());
        var context = Context();
        context.CachedDelegate = async (_, _, execution) =>
            await execution.ReportProgressAsync(42.5, "Processing demo batch… 42.5%");

        await handler.ExecuteTaskAsync(context, isDue: false);

        var progress = store.GetTail(context.TickerId)
            .Where(x => x.LifecycleEvent == TickerLifecycleEvent.Progress).ToArray();
        if (!accepted)
        {
            Assert.Empty(progress);
            return;
        }

        var line = Assert.Single(progress);
        Assert.Equal(TickerStatus.InProgress, line.Status);
        Assert.Equal("Progress 42.5% — Processing demo batch…", line.Message);
        Assert.Equal(2, line.Level);
    }

    [Theory]
    [InlineData(TickerType.TimeTicker)]
    [InlineData(TickerType.CronTickerOccurrence)]
    public async Task Rejected_terminal_write_never_emits_completion(TickerType type)
    {
        var store = new InMemoryTickerExecutionLogStore();
        var writer = new TickerLifecycleLogger(store, new(), _clock);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.SupportsAcknowledgedTerminalUpdates.Returns(true);
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(provider, _clock,
            Substitute.For<ITickerQNotificationHubSender>(), new SchedulerOptionsBuilder(), writer);
        var context = Context(type).SetProperty(x => x.Status, TickerStatus.Failed);
        if (type == TickerType.TimeTicker) await manager.UpdateTickerAsync(context); // zero affected rows
        else await Assert.ThrowsAsync<TickerTerminalUpdateNotAcknowledgedException>(() => manager.UpdateTickerAsync(context));
        Assert.Empty(store.GetTail(context.TickerId));
    }

    [Fact]
    public void Events_deduplicate_by_lease_attempt_and_type_without_merging_application_logs()
    {
        var store = new InMemoryTickerExecutionLogStore();
        var writer = new TickerLifecycleLogger(store, new(), _clock);
        var context = Context();
        writer.Started(context, 0); writer.Started(context, 0); writer.Started(context, 1);
        context.AcquisitionToken = Guid.NewGuid(); writer.Started(context, 0);
        context.Type = TickerType.CronTickerOccurrence; writer.Started(context, 0);
        store.Add(new() { TickerId = context.TickerId, Message = "same application message" });
        store.Add(new() { TickerId = context.TickerId, Message = "same application message" });
        Assert.Equal(6, store.GetTail(context.TickerId).Count);
        Assert.Equal(6, store.GetTail(context.TickerId).Select(x => x.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(TickerStatus.Done, 2)]
    [InlineData(TickerStatus.DueDone, 2)]
    [InlineData(TickerStatus.Failed, 4)]
    [InlineData(TickerStatus.Cancelled, 3)]
    [InlineData(TickerStatus.Skipped, 3)]
    public async Task Child_terminal_outcomes_use_saved_status_and_do_not_duplicate(TickerStatus status, int level)
    {
        var store = new InMemoryTickerExecutionLogStore();
        var writer = new TickerLifecycleLogger(store, new(), _clock);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.UpdateTimeTicker(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()).Returns(1);
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(provider, _clock,
            Substitute.For<ITickerQNotificationHubSender>(), new SchedulerOptionsBuilder(), writer);
        var child = Context().SetProperty(x => x.Status, status);
        child.ParentId = Guid.NewGuid(); child.ChainGeneration = Guid.NewGuid(); child.ElapsedTime = 1234;
        child.ExceptionDetails = "{\"Message\":\"Stopped for testing\",\"StackTrace\":\"not captured\"}";
        await manager.UpdateTickerAsync(child); await manager.UpdateTickerAsync(child);
        var line = Assert.Single(store.GetTail(child.TickerId));
        Assert.Equal(status, line.Status); Assert.Equal(level, line.Level);
        Assert.Contains("1.234s", line.Message); Assert.DoesNotContain("StackTrace", line.Message);
    }

    [Fact]
    public async Task Recurring_chain_reports_launch_and_each_new_job_without_claiming_children_completed()
    {
        var store = new InMemoryTickerExecutionLogStore();
        var writer = new TickerLifecycleLogger(store, new(), _clock);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.SupportsCronChains.Returns(true);
        provider.MaterializeCronChainAsync(Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationToken>())
            .Returns(CronChainDispatchResult.Created);
        var context = Context(TickerType.CronTickerOccurrence); context.ParentId = Guid.NewGuid();
        var child = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "Child" };
        var root = new TimeTickerEntity { Id = context.TickerId, Function = "Root", Children = [child] };
        provider.GetTimeTickerById(context.TickerId, Arg.Any<CancellationToken>()).Returns(root);
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(provider, _clock,
            Substitute.For<ITickerQNotificationHubSender>(), new SchedulerOptionsBuilder(), writer);
        Assert.True(await manager.TryDispatchCronChainAsync(context, false));
        Assert.Contains(store.GetTail(root.Id), x => x.LifecycleEvent == TickerLifecycleEvent.ChainLaunched);
        Assert.Contains(store.GetTail(root.Id), x => x.LifecycleEvent == TickerLifecycleEvent.Created);
        Assert.Equal(TickerLifecycleEvent.Created, Assert.Single(store.GetTail(child.Id)).LifecycleEvent);
        Assert.DoesNotContain(store.GetTail(root.Id), x => x.LifecycleEvent == TickerLifecycleEvent.Completed);
    }

    [Fact]
    public async Task Manual_cron_creation_is_captured_only_after_insert_succeeds()
    {
        var store = new InMemoryTickerExecutionLogStore();
        var writer = new TickerLifecycleLogger(store, new(), _clock);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var dispatcher = Substitute.For<ITickerQDispatcher>(); dispatcher.IsEnabled.Returns(true);
        var cron = new CronTickerEntity { Id = Guid.NewGuid(), Function = "Manual" };
        provider.GetCronTickerById(cron.Id, Arg.Any<CancellationToken>()).Returns(cron);
        provider.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns([]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => OnDemandCronExecution.RunAsync(provider, dispatcher, null,
            "partition", cron.Id, CancellationToken.None, lifecycle: writer));
        Assert.Empty(store.Pending(100));
        provider.InsertCronTickerOccurrences(Arg.Any<CronTickerOccurrenceEntity<CronTickerEntity>[]>(), Arg.Any<CancellationToken>()).Returns(1);
        var saved = await OnDemandCronExecution.RunAsync(provider, dispatcher, null, "partition", cron.Id,
            CancellationToken.None, lifecycle: writer);
        var line = Assert.Single(store.GetTail(saved.Id));
        Assert.Equal(TickerLifecycleEvent.Created, line.LifecycleEvent);
        Assert.Equal(new DateTimeOffset(saved.CreatedAt).ToUnixTimeMilliseconds(), line.UnixMs);
    }

    [Fact]
    public void Lifecycle_respects_switches_filter_redaction_and_capture_failure_isolation()
    {
        var options = new ExecutionLogOptions { IncludeLifecycleEvents = false };
        var store = new InMemoryTickerExecutionLogStore(options);
        var writer = new TickerLifecycleLogger(store, options, _clock);
        var context = Context();
        writer.Started(context, 0); Assert.Empty(store.GetTail(context.TickerId));
        options.IncludeLifecycleEvents = true; options.Enabled = false;
        writer.Started(context, 0); Assert.Empty(store.GetTail(context.TickerId));
        options.Enabled = true; options.MinimumLevel = LogLevel.Warning;
        writer.Started(context, 0); Assert.Empty(store.GetTail(context.TickerId));
        options.Redact = text => text.Replace("secret", "[redacted]");
        context.ExceptionDetails = "secret";
        context.SetProperty(x => x.Status, TickerStatus.Failed);
        writer.Committed(context);
        Assert.Contains("[redacted]", Assert.Single(store.GetTail(context.TickerId)).Message);
        options.Redact = _ => throw new InvalidOperationException();
        writer.Committed(context); // Never bubbles into the scheduler
        Assert.Single(store.GetTail(context.TickerId));
    }

    [Fact]
    public void Metadata_roundtrips_and_v1_archives_remain_readable()
    {
        var store = new InMemoryTickerExecutionLogStore();
        var writer = new TickerLifecycleLogger(store, new(), _clock);
        var context = Context(); writer.Started(context, 2);
        var record = ExecutionLogCodec.Encode(store.Snapshot(context.TickerId), "partition", TimeSpan.FromDays(1));
        var decoded = Assert.Single(ExecutionLogCodec.Decode(record));
        Assert.Equal(TickerLifecycleEvent.Started, decoded.LifecycleEvent);
        Assert.Equal(TickerStatus.InProgress, decoded.Status); Assert.Equal(3, decoded.Attempt);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
        using (var binary = new BinaryWriter(gzip, Encoding.UTF8))
        {
            binary.Write(1); binary.Write(1);
            binary.Write(Guid.NewGuid().ToByteArray()); binary.Write(123L); binary.Write(2);
            foreach (var value in new[] { "old log", "Test", "Example" })
            { var bytes = Encoding.UTF8.GetBytes(value); binary.Write(bytes.Length); binary.Write(bytes); }
            binary.Write(false);
        }
        record.Payload = output.ToArray();
        var old = Assert.Single(ExecutionLogCodec.Decode(record));
        Assert.Equal("old log", old.Message); Assert.Equal(TickerLifecycleEvent.None, old.LifecycleEvent); Assert.Null(old.Status);
    }
}
