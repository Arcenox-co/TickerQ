using NSubstitute;
using TickerQ.Utilities.Integrations;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Integrations.Monitoring;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Integrations.Tests;

public partial class MonitoringTests
{
    [Fact]
    public void LateFailuresAndInterveningSuccess_DoNotInventAnIncident()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), null,
            [Completed(TickerStatus.Failed, 20), Completed(TickerStatus.Failed, 21)], []));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(60), null, [], [])
        { Replayed = [Completed(TickerStatus.Failed, 10), Completed(TickerStatus.Done, 15)] });
        Assert.Empty(state.Deliveries);
        Assert.Equal(2, state.Monitors[0].ConsecutiveFailures.Count);
    }

    [Fact]
    public void LateFailure_OpensAtTheActualThresholdTime()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), null,
            [Completed(TickerStatus.Failed, 20), Completed(TickerStatus.Failed, 21)], []));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(60), null, [], [])
        { Replayed = [Completed(TickerStatus.Failed, 10)] });
        Assert.Single(state.Deliveries);
        Assert.Equal(Now.AddSeconds(21), state.Monitors[0].View.OpenedAtUtc);
    }

    [Fact]
    public void LateSuccess_RecoversAtObservationTimeAndKeepsLaterFailures()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), null,
            [Completed(TickerStatus.Failed, 11), Completed(TickerStatus.Failed, 12), Completed(TickerStatus.Failed, 21)], []));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(60), null, [], [])
        { Replayed = [Completed(TickerStatus.Done, 15)] });
        AssertPair(state, "consecutive_failures");
        Assert.Equal(Now.AddSeconds(60), state.Monitors[0].View.RecoveredAtUtc);
        Assert.Equal(1, state.Deliveries[1].Message.FailureCount);
        Assert.Equal(Now.AddSeconds(21), Assert.Single(state.Monitors[0].ConsecutiveFailures));
    }

    [Fact]
    public void LateRecoveryAndNewFailureStreak_KeepTransitionTimestampsInOrderAcrossRestart()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), null,
            [Completed(TickerStatus.Failed, 11), Completed(TickerStatus.Failed, 12), Completed(TickerStatus.Failed, 21)], []));
        state = Restart(state);
        var observation = new MonitoringObservation(Now.AddSeconds(60), null,
            [Completed(TickerStatus.Failed, 31), Completed(TickerStatus.Failed, 32)], [])
        { Replayed = [Completed(TickerStatus.Done, 15)] };
        Apply(state, config, observation);

        Assert.Equal(3, state.Deliveries.Count);
        Assert.Equal(new[] { "monitoring_consecutive_failures", "monitoring_recovered", "monitoring_consecutive_failures" },
            state.Deliveries.Select(x => x.Message.Event.Kind));
        var occurred = state.Deliveries.Select(x => x.Message.Event.OccurredAtUtc).ToArray();
        Assert.Equal(occurred.OrderBy(at => at), occurred);
        Assert.Equal(Now.AddSeconds(60), state.Monitors[0].View.OpenedAtUtc);
        state = Restart(state);
        Apply(state, config, observation with { AtUtc = Now.AddSeconds(90) });
        Assert.Equal(3, state.Deliveries.Count);
    }

    [Fact]
    public void SharedFunctionPage_ReplaysUnseenRowsBehindThisMonitorsCheckpoint()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(60), null, [], []));
        // A sibling monitor with an older checkpoint caused the shared source to return
        // these rows in Recent rather than in its separately queried overlap.
        Apply(state, config, new(Now.AddSeconds(90), null,
            [Completed(TickerStatus.Failed, 10), Completed(TickerStatus.Failed, 20), Completed(TickerStatus.Failed, 30)], []));
        Assert.Single(state.Deliveries);
        Assert.Equal(3, state.Monitors[0].ConsecutiveFailures.Count);
    }

    [Fact]
    public async Task CatchUpPage_ReadsOverlapBeforeAdvancingAnIntermediateCheckpoint()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var late = new TickerMonitoringExecution { Id = Guid.NewGuid(), Status = TickerStatus.Failed, CompletedUtc = Now.AddSeconds(-1) };
        var rows = Enumerable.Range(1, TickerMonitoringSnapshot.PageSize + 1)
            .Select(index => new TickerMonitoringExecution
            {
                Id = Guid.NewGuid(), Status = TickerStatus.Failed, CompletedUtc = Now.AddTicks(index)
            }).Append(late).ToArray();
        provider.ReadMonitoringSnapshotAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TickerMonitoringSnapshot
            {
                Completed = rows.Where(row => row.CompletedUtc > call.ArgAt<DateTime>(1) && row.CompletedUtc <= call.ArgAt<DateTime>(2))
                    .OrderBy(row => row.CompletedUtc).Take(TickerMonitoringSnapshot.PageSize + 1).ToArray()
            }));
        var source = new MonitoringExecutionSource<TimeTickerEntity, CronTickerEntity>(provider);
        var observation = await source.ReadAsync(Config().Monitors[0], Now.AddMinutes(1), default, Now);
        Assert.True(observation.HasMore);
        Assert.Equal(late.Id, Assert.Single(observation.Replayed).Id);
        Assert.Equal(Now.AddTicks(TickerMonitoringSnapshot.PageSize), observation.ThroughUtc);
    }

    [Fact]
    public void LateOldSuccess_DoesNotRecoverAnUnrelatedRunningExecution()
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 60;
        var state = new MonitoringState(); var running = new MonitorRunning(Guid.NewGuid(), Guid.NewGuid());
        Apply(state, config, new(Now, null, [], [running]));
        Apply(state, config, new(Now.AddSeconds(60), null, [], [running]));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(90), null, [], [running])
        { Replayed = [Completed(TickerStatus.Done, 30)] });
        Assert.Single(state.Deliveries);
        Assert.Equal("incident", state.Monitors[0].View.Status);
    }

    [Fact]
    public void LateCompletionOfObservedRun_CanRecoverTheIncident()
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 60;
        var state = new MonitoringState(); var running = new MonitorRunning(Guid.NewGuid(), Guid.NewGuid());
        Apply(state, config, new(Now, null, [], [running]));
        Apply(state, config, new(Now.AddSeconds(60), null, [], [running]));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(90), null, [], [])
        { Replayed = [new(running.Id, TickerStatus.Done, Now.AddSeconds(59), Generation: running.Generation)] });
        AssertPair(state, "long_running");
    }

    [Fact]
    public async Task CompletionCommittedBehindCheckpoint_IsObservedOnceAcrossRestart()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(o => o.UseApplicationNamespace("late-commit-" + Guid.NewGuid()).UseReconciliationEpoch(1).AddIntegrations());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scheduler = container.GetRequiredService<SchedulerOptionsBuilder>();
        await provider.BeginReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        await provider.CommitReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        var config = Config(); var rule = config.Monitors[0]; var state = new MonitoringState();
        var source = container.GetRequiredService<IMonitoringExecutionSource>();
        Apply(state, config, await source.ReadAsync(rule, Now, default));
        Apply(state, config, await source.ReadAsync(rule, Now.AddSeconds(30), default, Now));
        // These terminal rows only become visible AFTER the scanner passed their timestamps.
        await provider.AddTimeTickers(Enumerable.Range(1, 3).Select(i => new TimeTickerEntity
        {
            Function = rule.Function, Status = TickerStatus.Failed, ExecutedAt = Now.AddSeconds(i)
        }).ToArray());
        Apply(state, config, await source.ReadAsync(rule, Now.AddSeconds(60), default, Now.AddSeconds(30)));
        Assert.Single(state.Deliveries);
        state = Restart(state);
        await provider.AddTimeTickers([new() { Function = rule.Function, Status = TickerStatus.Done, ExecutedAt = Now.AddSeconds(40) }]);
        Apply(state, config, await source.ReadAsync(rule, Now.AddSeconds(90), default, Now.AddSeconds(60)));
        AssertPair(state, "consecutive_failures");
        var ids = state.Deliveries.Select(x => x.Id).ToArray();
        state = Restart(state);
        Apply(state, config, await source.ReadAsync(rule, Now.AddSeconds(120), default, Now.AddSeconds(90)));
        Assert.Equal(ids, state.Deliveries.Select(x => x.Id));
    }

    [Fact]
    public void LateOldFailures_DoNotReopenAfterNewerSuccess()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), Now.AddSeconds(20), [Completed(TickerStatus.Done, 20)], []));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(60), null, [], [])
        { Replayed = [Completed(TickerStatus.Failed, 1), Completed(TickerStatus.Failed, 2), Completed(TickerStatus.Failed, 3)] });
        Assert.Empty(state.Deliveries);
        Assert.Empty(state.Monitors[0].ConsecutiveFailures);
    }

    [Fact]
    public void LateOldSuccess_DoesNotClearNewerFailureIncident()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.AddSeconds(30), null,
            [Completed(TickerStatus.Failed, 21), Completed(TickerStatus.Failed, 22), Completed(TickerStatus.Failed, 23)], []));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(60), null, [], [])
        { Replayed = [Completed(TickerStatus.Done, 10)] });
        Assert.Single(state.Deliveries);
        Assert.Equal("incident", state.Monitors[0].View.Status);
    }

    [Fact]
    public void ReplayIdentity_IncludesGenerationAndTickerType()
    {
        var config = Config(); var state = new MonitoringState(); var id = Guid.NewGuid();
        Apply(state, config, new(Now, null, [], []));
        var first = new MonitorExecution(id, TickerStatus.Failed, Now.AddSeconds(5), Generation: Guid.NewGuid());
        Apply(state, config, new(Now.AddSeconds(30), null, [first], []));
        Apply(state, config, new MonitoringObservation(Now.AddSeconds(60), null, [], [])
        { Replayed = [first, first with { Generation = Guid.NewGuid() }, first with { IsCron = true }] });
        Assert.Single(state.Deliveries);
        Assert.Equal(3, state.Monitors[0].ConsecutiveFailures.Count);
    }
}
