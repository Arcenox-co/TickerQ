using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Integrations.Monitoring;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Integrations.Tests;

public partial class MonitoringTests
{
    private static MonitorExecution Completed(TickerStatus status, int seconds, long elapsed = 0, Guid? id = null)
        => new(id ?? Guid.NewGuid(), status, Now.AddSeconds(seconds), elapsed);

    private static MonitoringState Restart(MonitoringState state)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(state, IntegrationJsonContext.Default.MonitoringState),
            IntegrationJsonContext.Default.MonitoringState)!;

    private static void AssertPair(MonitoringState state, string kind)
    {
        Assert.Equal(new[] { "monitoring_" + kind, "monitoring_recovered" }, state.Deliveries.Select(x => x.Message.Event.Kind));
        Assert.Equal(state.Deliveries[0].Message.IncidentId, state.Deliveries[1].Message.IncidentId);
        Assert.NotEqual(state.Deliveries[0].Message.EventId, state.Deliveries[1].Message.EventId);
        Assert.Equal("healthy", state.Monitors[0].View.Status);
    }

    [Fact]
    public void RapidFailuresAndRecovery_AreReplayedInOrder_OnceAcrossRestart()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        var observation = new MonitoringObservation(Now.AddSeconds(30), Now.AddSeconds(10),
            [Completed(TickerStatus.Done, 10), Completed(TickerStatus.Failed, 3), Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2)], []);
        Apply(state, config, observation);
        AssertPair(state, "consecutive_failures");
        Assert.Equal(Now.AddSeconds(3), state.Deliveries[0].Message.Event.OccurredAtUtc);
        Assert.Equal(Now.AddSeconds(10), state.Deliveries[1].Message.Event.OccurredAtUtc);
        var ids = state.Deliveries.Select(x => x.Message.EventId).ToArray();
        state = Restart(state);
        Apply(state, config, observation with { AtUtc = Now.AddMinutes(1) });
        Assert.Equal(ids, state.Deliveries.Select(x => x.Message.EventId));
    }

    [Fact]
    public void MultipleFastCycles_AndMoreThan21Completions_AreNotCollapsed()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        var events = Enumerable.Range(1, 40).Select(i => Completed(i % 4 == 0 ? TickerStatus.Done : TickerStatus.Failed, i)).ToArray();
        Apply(state, config, new(Now.AddMinutes(1), Now.AddSeconds(40), events, []));
        Assert.Equal(20, state.Deliveries.Count);
        Assert.Equal(10, state.Deliveries.Select(x => x.Message.IncidentId).Distinct().Count());
        for (var i = 0; i < 20; i += 2)
            Assert.Equal(state.Deliveries[i].Message.IncidentId, state.Deliveries[i + 1].Message.IncidentId);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(10, true)]
    public void Deadline_UsesActualCutoff_IncludingGrace_NotScanTime(int successOffsetSeconds, bool missed)
    {
        var config = Config("deadline"); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        var cutoff = Now.AddMinutes(70);
        var success = cutoff.AddSeconds(successOffsetSeconds);
        Apply(state, config, new(cutoff.AddSeconds(30), success, [new(Guid.NewGuid(), TickerStatus.Done, success)], []));
        if (missed)
        {
            AssertPair(state, "deadline");
            Assert.Equal(cutoff, state.Deliveries[0].Message.Event.OccurredAtUtc);
            Assert.Equal(success, state.Deliveries[1].Message.Event.OccurredAtUtc);
            Assert.Equal(Now.AddHours(1), state.Deliveries[0].Message.DeadlineUtc);
        }
        else Assert.Empty(state.Deliveries);
        state = Restart(state);
        Apply(state, config, new(cutoff.AddMinutes(1), success, [], []));
        Assert.Equal(missed ? 2 : 0, state.Deliveries.Count);
    }

    [Fact]
    public void Deadline_OnTimeSuccessIsNotHiddenByAnotherLateSuccess()
    {
        var config = Config("deadline"); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddMinutes(71), Now.AddMinutes(70).AddSeconds(10),
            [Completed(TickerStatus.Done, 4200 + 10), Completed(TickerStatus.Done, 4190)], []));
        Assert.Empty(state.Deliveries);
        Assert.Equal("healthy", state.Monitors[0].View.Status);
    }

    [Fact]
    public void CompletedSlowRun_IsDetected_EvenWhenNeverSeenRunning()
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 5;
        var state = new MonitoringState(); Apply(state, config, new(Now, null, [], []));
        var completed = Completed(TickerStatus.Done, 21, 20000);
        Apply(state, config, new(Now.AddSeconds(30), Now.AddSeconds(21), [completed], []));
        AssertPair(state, "long_running");
        Assert.Equal(20, state.Deliveries[0].Message.ObservedDurationSeconds);
        Assert.Null(state.Deliveries[1].Message.ObservedDurationSeconds);
        state = Restart(state);
        Apply(state, config, new(Now.AddSeconds(60), Now.AddSeconds(21), [completed], []));
        Assert.Equal(2, state.Deliveries.Count);
    }

    [Fact]
    public void ObservedSlowRun_UsesCompletionTime_WhenNoDurationWasRecorded()
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 5;
        var state = new MonitoringState(); var id = Guid.NewGuid();
        Apply(state, config, new(Now, null, [], [new(id, null)]));
        state = Restart(state);
        Apply(state, config, new(Now.AddSeconds(30), Now.AddSeconds(20), [Completed(TickerStatus.Done, 20, id: id)], []));
        AssertPair(state, "long_running");
        Assert.Equal(20, state.Deliveries[0].Message.ObservedDurationSeconds);
    }

    [Theory]
    [InlineData(TickerStatus.Failed)]
    [InlineData(TickerStatus.Cancelled)]
    public void CompletedSlowFailure_DoesNotRecoverUntilLaterSuccess(TickerStatus status)
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 5;
        var state = new MonitoringState(); Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), null, [Completed(status, 20, 20000)], []));
        Assert.Single(state.Deliveries); Assert.NotNull(state.Monitors[0].IncidentId);
        Apply(state, config, new(Now.AddSeconds(60), Now.AddSeconds(40), [Completed(TickerStatus.Done, 40, 1000)], []));
        AssertPair(state, "long_running");
    }

    [Fact]
    public void FailureStreak_SurvivesScanBoundariesAndRestart_CancellationBreaksIt()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(10), null, [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2)], []));
        state = Restart(state);
        Apply(state, config, new(Now.AddSeconds(20), null, [Completed(TickerStatus.Cancelled, 11), Completed(TickerStatus.Failed, 12)], []));
        Assert.Empty(state.Deliveries);
        Apply(state, config, new(Now.AddSeconds(30), null, [Completed(TickerStatus.Failed, 21), Completed(TickerStatus.Failed, 22)], []));
        Assert.Single(state.Deliveries);
        Assert.Equal(Now.AddSeconds(22), state.Deliveries[0].Message.Event.OccurredAtUtc);
    }

    [Fact]
    public void FullDeliveryQueue_PersistsBothTransitionsAndProgress_AcrossRestart()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        state.Deliveries = Enumerable.Range(0, 256).Select(_ => new MonitorDelivery {
            MonitorId = config.Monitors[0].Id, Fingerprint = MonitorEvaluation.Fingerprint(config.Monitors[0]),
            DestinationId = config.Destinations[0].Id, Message = IntegrationEngine.Sample(new())
        }).ToList();
        var observation = new MonitoringObservation(Now.AddSeconds(30), Now.AddSeconds(10),
            [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2), Completed(TickerStatus.Failed, 3), Completed(TickerStatus.Done, 10)], []);
        Apply(state, config, observation);
        Assert.Equal(256, state.Deliveries.Count);
        Assert.Equal(2, state.Monitors[0].PendingTransitions.Count);
        var ids = state.Monitors[0].PendingTransitions.Select(x => x.Message.EventId).ToArray();
        state = Restart(state);
        // Blocked monitors retain their next cursor instead of consuming further history.
        Apply(state, config, observation with { AtUtc = Now.AddSeconds(60) });
        Assert.Equal(Now.AddSeconds(30), state.Monitors[0].ProcessedThroughUtc);
        state.Deliveries.Clear(); // existing backlog was delivered
        Apply(state, config, observation with { AtUtc = Now.AddSeconds(90) });
        AssertPair(state, "consecutive_failures");
        Assert.Empty(state.Monitors[0].PendingTransitions);
        Assert.Equal(ids, state.Deliveries.Select(x => x.Message.EventId));
    }

    [Fact]
    public void FullQueue_DetectedFailureSurvivesRecoveryOnNextScan()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        state.Deliveries = Enumerable.Range(0, 256).Select(_ => new MonitorDelivery {
            MonitorId = config.Monitors[0].Id, Fingerprint = MonitorEvaluation.Fingerprint(config.Monitors[0]),
            DestinationId = config.Destinations[0].Id, Message = IntegrationEngine.Sample(new())
        }).ToList();
        Apply(state, config, new(Now.AddSeconds(30), null,
            [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2), Completed(TickerStatus.Failed, 3)], []));
        state = Restart(state);
        state.Deliveries.Clear();
        Apply(state, config, new(Now.AddSeconds(60), Now.AddSeconds(40), [Completed(TickerStatus.Done, 40)], []));
        AssertPair(state, "consecutive_failures");
    }

    [Fact]
    public void FailureQueryError_DoesNotAdvanceCursorOrInventRecovery()
    {
        var config = Config(); var state = new MonitoringState(); Apply(state, config, Failure(Now));
        Apply(state, config, new(Now.AddMinutes(1), null, [], [], "Unavailable"));
        Assert.Equal(Now, state.Monitors[0].ProcessedThroughUtc);
        Assert.NotNull(state.Monitors[0].IncidentId);
        Assert.Single(state.Deliveries);
    }

    [Fact]
    public void SharedOlderPage_DoesNotMoveAnotherMonitorsCheckpointBackwards()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddMinutes(2), null, [], []));
        Apply(state, config, new MonitoringObservation(Now.AddMinutes(3), null,
            [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2), Completed(TickerStatus.Failed, 3)], [])
        { ThroughUtc = Now.AddMinutes(1), HasMore = true });
        Assert.Equal(Now.AddMinutes(2), state.Monitors[0].ProcessedThroughUtc);
        Assert.Empty(state.Deliveries);
    }

    [Fact]
    public void EqualCompletionTimestamps_ConservativelyBreakStreak_RegardlessOfInputOrder()
    {
        var config = Config();
        var executions = new[] { Completed(TickerStatus.Failed, 10), Completed(TickerStatus.Failed, 10),
            Completed(TickerStatus.Failed, 10), Completed(TickerStatus.Done, 10) };
        foreach (var rows in new[] { executions, executions.Reverse().ToArray() })
        {
            var state = new MonitoringState(); Apply(state, config, new(Now, null, [], []));
            Apply(state, config, new(Now.AddSeconds(30), Now.AddSeconds(10), rows, []));
            Assert.Empty(state.Deliveries);
            Assert.Empty(state.Monitors[0].ConsecutiveFailures);
        }
    }

    [Fact]
    public void QueuePressure_PartialDestinationFanOut_PreservesPairingAndOrder()
    {
        var config = Config(); var second = new IntegrationDestination { Name = "Second", Provider = "webhook" };
        config.Destinations.Add(second); config.Monitors[0].DestinationIds.Add(second.Id);
        var state = new MonitoringState(); Apply(state, config, new(Now, null, [], []));
        state.Deliveries = Enumerable.Range(0, 255).Select(_ => new MonitorDelivery {
            MonitorId = config.Monitors[0].Id, Fingerprint = MonitorEvaluation.Fingerprint(config.Monitors[0]),
            DestinationId = config.Destinations[0].Id, Message = IntegrationEngine.Sample(new())
        }).ToList();
        var observation = new MonitoringObservation(Now.AddSeconds(30), Now.AddSeconds(10),
            [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2), Completed(TickerStatus.Failed, 3), Completed(TickerStatus.Done, 10)], []);
        Apply(state, config, observation);
        Assert.Equal(256, state.Deliveries.Count);
        Assert.Equal(3, state.Monitors[0].PendingTransitions.Sum(x => x.DestinationIds.Count));
        state = Restart(state);
        state.Deliveries.RemoveRange(0, 255);
        Apply(state, config, observation with { AtUtc = Now.AddMinutes(1) });
        Assert.Equal(4, state.Deliveries.Count);
        Assert.Equal(2, state.Deliveries.Select(x => x.Message.EventId).Distinct().Count());
        foreach (var destination in config.Destinations)
            Assert.Equal(new[] { "monitoring_consecutive_failures", "monitoring_recovered" },
                state.Deliveries.Where(x => x.DestinationId == destination.Id).Select(x => x.Message.Event.Kind));
    }

    [Fact]
    public async Task RapidCycle_TwoReplicas_DeliversOnePair_AndRestartDoesNotReplayIt()
    {
        var store = new AtomicStore(); var protection = new EphemeralDataProtectionProvider(); var options = new IntegrationOptions();
        var configuration = new IntegrationConfigurationService(store, protection, options, [new WebhookFormatter()]);
        await configuration.SaveAsync(Config(), default);
        var clock = new Clock(); var source = new SnapshotSource { Observation = new(Now, null, [], []) };
        var handler = new Handler(); var runtime = new MonitoringStateStore(store, protection);
        var engine = new IntegrationEngine(options, configuration, new(new Clients(handler), options), NullLogger<IntegrationEngine>.Instance, runtime);
        IntegrationMonitoringService Worker() => new(configuration, runtime, source, engine, options, clock, NullLogger<IntegrationMonitoringService>.Instance);
        await Worker().TickAsync(default, deliver: false);
        clock.UtcNow = Now.AddSeconds(30);
        source.Observation = new(clock.UtcNow, Now.AddSeconds(10),
            [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2), Completed(TickerStatus.Failed, 3), Completed(TickerStatus.Done, 10)], []);
        await Task.WhenAll(Worker().TickAsync(default, deliver: false), Worker().TickAsync(default, deliver: false));
        AssertPair((await runtime.ReadAsync(default)).State, "consecutive_failures");
        await Task.WhenAll(Worker().DeliverPendingAsync(default), Worker().DeliverPendingAsync(default));
        Assert.Equal(2, handler.Count);
        clock.UtcNow = Now.AddMinutes(1);
        await Worker().TickAsync(default);
        Assert.Equal(2, handler.Count);
        Assert.Empty((await runtime.ReadAsync(default)).State.Deliveries);
    }

    private sealed class SnapshotSource : IMonitoringExecutionSource
    {
        public MonitoringObservation Observation { get; set; } = null!;
        public Task<MonitoringObservation> ReadAsync(IntegrationMonitor rule, DateTime now, CancellationToken ct, DateTime? afterUtc = null)
            => Task.FromResult(Observation with { AtUtc = now });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecutionSource_BoundedQueriesReportUnavailableWhenLimitExceeded(bool simultaneousCompletions)
    {
        var services = new ServiceCollection();
        services.AddTickerQ(o => o.UseApplicationNamespace("monitor-limits-" + Guid.NewGuid()).UseReconciliationEpoch(1).AddIntegrations());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scheduler = container.GetRequiredService<SchedulerOptionsBuilder>();
        await provider.BeginReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        await provider.CommitReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        var rule = Config().Monitors[0];
        var jobs = Enumerable.Range(0, simultaneousCompletions ? 513 : 1001).Select(_ => new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = rule.Function,
            Status = simultaneousCompletions ? TickerStatus.Done : TickerStatus.InProgress,
            ExecutedAt = simultaneousCompletions ? Now : null
        }).ToArray();
        await provider.AddTimeTickers(jobs);
        var observation = await container.GetRequiredService<IMonitoringExecutionSource>().ReadAsync(rule, Now, default);
        Assert.Contains(simultaneousCompletions ? "share one timestamp" : "active executions", observation.Error);
        Assert.Null(observation.ThroughUtc);
        Assert.Null(observation.LastSuccessUtc);
        Assert.Empty(observation.Recent);
    }

    [Fact]
    public async Task ExecutionSource_PagesOldestFirst_WithoutSplittingTimestampGroups()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(o => o.UseApplicationNamespace("monitor-paging-" + Guid.NewGuid()).UseReconciliationEpoch(1).AddIntegrations());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scheduler = container.GetRequiredService<SchedulerOptionsBuilder>();
        await provider.BeginReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        await provider.CommitReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        var rule = Config().Monitors[0];
        var source = container.GetRequiredService<IMonitoringExecutionSource>();
        var size = MonitoringExecutionSource<TimeTickerEntity, CronTickerEntity>.PageSize;
        var jobs = Enumerable.Range(1, size + 10).Select(i => new TimeTickerEntity {
            Id = Guid.NewGuid(), Function = rule.Function, Status = i % 4 == 0 ? TickerStatus.Done : TickerStatus.Failed,
            ExecutedAt = Now.AddSeconds(i < size ? i : size), ElapsedTime = 1234
        }).ToArray();
        await provider.AddTimeTickers(jobs);
        var end = Now.AddHours(1);
        var first = await source.ReadAsync(rule, end, default, Now);
        Assert.True(first.HasMore); Assert.Null(first.Error);
        Assert.Equal(size - 1, first.Recent.Length);
        Assert.All(first.Recent, x => Assert.Equal(1234, x.ElapsedMilliseconds));
        var second = await source.ReadAsync(rule, end, default, first.ThroughUtc);
        Assert.False(second.HasMore); Assert.Null(second.Error);
        Assert.Equal(11, second.Recent.Length);
        Assert.Equal(jobs.Length, first.Recent.Concat(second.Recent).Select(x => x.Id).Distinct().Count());
        Assert.Empty((await source.ReadAsync(rule, end, default, second.ThroughUtc)).Recent);
    }
}
