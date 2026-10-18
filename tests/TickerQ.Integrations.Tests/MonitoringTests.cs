using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Integrations.Monitoring;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Integrations.Tests;

public partial class MonitoringTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 7, 0, 0, DateTimeKind.Utc);
    private static IntegrationConfiguration Config(string kind = "consecutive_failures")
    {
        var destination = new IntegrationDestination { Name = "Operations", Provider = "webhook", Url = "https://example.com/secret" };
        return new() { Destinations = [destination], Monitors = [new() { Name = "Invoices", Function = "Invoices.Run", Kind = kind, DestinationIds = [destination.Id], FailureThreshold = 3 }] };
    }
    private static MonitoringObservation Failure(DateTime now) => new(now, null,
        Enumerable.Range(0, 3).Select(i => new MonitorExecution(Guid.NewGuid(), TickerStatus.Failed, now.AddSeconds(-i))).ToArray(), []);
    private static void Apply(MonitoringState state, IntegrationConfiguration config, MonitoringObservation observation)
        => IntegrationMonitoringService.ApplyObservations(state, config,
            new Dictionary<Guid, MonitoringObservation> { [config.Monitors[0].Id] = observation }, new(), observation.AtUtc);

    [Fact]
    public void Incident_AlertsOnce_RecoversOnce_AndPairsMessages()
    {
        var config = Config(); var state = new MonitoringState();
        Apply(state, config, Failure(Now)); Apply(state, config, Failure(Now.AddMinutes(1)));
        var alert = Assert.Single(state.Deliveries);
        Assert.Equal("monitoring_consecutive_failures", alert.Message.Event.Kind);
        Assert.Equal("incident", state.Monitors[0].View.Status);
        var success = new MonitoringObservation(Now.AddMinutes(2), Now.AddMinutes(2), [new(Guid.NewGuid(), TickerStatus.Done, Now.AddMinutes(2))], []);
        Apply(state, config, success); Apply(state, config, success);
        Assert.Equal(2, state.Deliveries.Count);
        Assert.Equal(alert.Message.IncidentId, state.Deliveries[1].Message.IncidentId);
        Assert.Equal("monitoring_recovered", state.Deliveries[1].Message.Event.Kind);
        Assert.Equal("healthy", state.Monitors[0].View.Status);
    }

    [Fact]
    public void MissingHistory_AndQueryErrors_DoNotClearAnIncident()
    {
        var config = Config(); var state = new MonitoringState(); Apply(state, config, Failure(Now));
        Apply(state, config, new(Now.AddDays(3), null, [], []));
        Apply(state, config, new(Now.AddDays(3), null, [], [], "Database unavailable"));
        Assert.Equal("incident", state.Monitors[0].View.Status); Assert.Single(state.Deliveries);
    }

    [Fact]
    public void CancelledExecution_BreaksFailureStreak_ButIsNotSuccessfulRecovery()
    {
        var config = Config(); var state = new MonitoringState(); Apply(state, config, Failure(Now));
        Apply(state, config, new(Now.AddMinutes(1), null, [new(Guid.NewGuid(), TickerStatus.Cancelled, Now.AddMinutes(1))], []));
        Assert.Equal("incident", state.Monitors[0].View.Status); Assert.Single(state.Deliveries);
    }

    [Fact]
    public void Deadline_StartsAtNextDeadline_AlertsWithoutAnyExecutions_AndRecoversLate()
    {
        var config = Config("deadline"); var state = new MonitoringState();
        Apply(state, config, new(Now, null, [], []));
        Assert.Empty(state.Deliveries);
        Apply(state, config, new(Now.AddMinutes(70), null, [], []));
        Assert.Single(state.Deliveries);
        Apply(state, config, new(Now.AddMinutes(75), Now.AddMinutes(74), [], []));
        Assert.Equal(2, state.Deliveries.Count); Assert.Equal("healthy", state.Monitors[0].View.Status);
        // A captured success remains usable when retention removes the original row.
        Apply(state, config, new(Now.AddMinutes(90), null, [], []));
        Assert.Equal(2, state.Deliveries.Count);
        Apply(state, config, new(Now.AddDays(1).AddMinutes(70), null, [], []));
        Assert.Equal(3, state.Deliveries.Count);
    }

    [Fact]
    public void NewDeadline_AfterCutoff_DoesNotAlertRetroactively()
    {
        var config = Config("deadline"); var state = new MonitoringState();
        Apply(state, config, new(Now.AddHours(4), null, [], []));
        Assert.Empty(state.Deliveries); Assert.Equal("waiting", state.Monitors[0].View.Status);
    }

    [Fact]
    public void Deadline_GraceCrossingMidnight_DoesNotAlertBeforeGraceEnds()
    {
        var config = Config("deadline"); var state = new MonitoringState();
        config.Monitors[0].DeadlineLocalTime = "23:30";
        config.Monitors[0].GraceMinutes = 120;
        Apply(state, config, new(Now, null, [], []));
        Apply(state, config, new(Now.Date.AddDays(1).AddMinutes(45), null, [], []));
        Assert.Empty(state.Deliveries);
        Apply(state, config, new(Now.Date.AddDays(1).AddMinutes(90), null, [], []));
        Assert.Single(state.Deliveries);
    }

    [Fact]
    public void Deadline_UsesRuleTimeZone_AndHandlesDstGapsAndRepeatedTimes()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc), MonitorEvaluation.ToUtc(new(2026, 3, 29, 2, 30, 0), zone));
        Assert.Equal(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), MonitorEvaluation.ToUtc(new(2026, 10, 25, 2, 30, 0), zone));
        var config = Config("deadline"); config.Monitors[0].TimeZoneId = "Europe/Berlin";
        var state = new MonitoringState(); Apply(state, config, new(Now.AddHours(-2), null, [], []));
        Apply(state, config, new(Now.AddMinutes(-50), null, [], []));
        Assert.Single(state.Deliveries); // 08:10 Berlin = 06:10 UTC in October.
    }

    [Fact]
    public void LongRunning_UsesObservedInProgressTime_ResetsForNewAcquisition_AndPersistsAcrossReads()
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 60;
        var state = new MonitoringState(); var id = Guid.NewGuid(); var generation = Guid.NewGuid();
        Apply(state, config, new(Now, null, [], [new(id, generation)]));
        Apply(state, config, new(Now.AddSeconds(59), null, [], [new(id, generation)])); Assert.Empty(state.Deliveries);
        generation = Guid.NewGuid();
        Apply(state, config, new(Now.AddMinutes(1), null, [], [new(id, generation)])); Assert.Empty(state.Deliveries);
        state = JsonSerializer.Deserialize(JsonSerializer.Serialize(state, IntegrationJsonContext.Default.MonitoringState), IntegrationJsonContext.Default.MonitoringState)!;
        Apply(state, config, new(Now.AddMinutes(2), null, [], [new(id, generation)])); Assert.Single(state.Deliveries);
        Apply(state, config, new(Now.AddMinutes(3), Now.AddMinutes(3), [new(id, TickerStatus.Done, Now.AddMinutes(3))], []));
        Assert.Equal(2, state.Deliveries.Count);
    }

    [Fact]
    public void DisabledOrEditedMonitors_DiscardPendingAlerts_AndResetObservationWindow()
    {
        var config = Config(); var state = new MonitoringState(); Apply(state, config, Failure(Now));
        config.Monitors[0].Enabled = false; Apply(state, config, Failure(Now));
        Assert.Empty(state.Deliveries); Assert.Equal("paused", state.Monitors[0].View.Status);
        config.Monitors[0].Enabled = true; config.Monitors[0].FailureThreshold = 4; Apply(state, config, Failure(Now));
        Assert.Empty(state.Deliveries); Assert.Equal("healthy", state.Monitors[0].View.Status);
    }

    [Fact]
    public void RecoveryCanBeDisabled_AndBacklogDoesNotLoseTransitions()
    {
        var config = Config(); config.Monitors[0].NotifyRecovery = false;
        var state = new MonitoringState(); Apply(state, config, Failure(Now));
        Apply(state, config, new(Now.AddMinutes(1), Now.AddMinutes(1), [new(Guid.NewGuid(), TickerStatus.Done, Now.AddMinutes(1))], []));
        Assert.Single(state.Deliveries); Assert.Equal("healthy", state.Monitors[0].View.Status);
        // Fill with valid records so pruning does not remove them.
        state.Deliveries = Enumerable.Range(0, 256).Select(_ => new MonitorDelivery { MonitorId = config.Monitors[0].Id,
            Fingerprint = MonitorEvaluation.Fingerprint(config.Monitors[0]), DestinationId = config.Destinations[0].Id }).ToList();
        Apply(state, config, Failure(Now.AddMinutes(2)));
        Assert.NotNull(state.Monitors[0].IncidentId);
        Assert.Single(state.Monitors[0].PendingTransitions);
        state.Deliveries.Clear(); Apply(state, config, Failure(Now.AddMinutes(3))); Assert.Single(state.Deliveries);
    }

    [Fact]
    public async Task PersistedOutbox_TwoReplicas_SendOneAlert_AndRestartDoesNotReopenIt()
    {
        var store = new AtomicStore(); var protection = new EphemeralDataProtectionProvider(); var options = new IntegrationOptions();
        var configService = new IntegrationConfigurationService(store, protection, options, [new WebhookFormatter()]);
        await configService.SaveAsync(Config(), default);
        var clock = new Clock(); var source = new Source(clock); var handler = new Handler();
        IntegrationMonitoringService Worker() {
            var runtime = new MonitoringStateStore(store, protection);
            var engine = new IntegrationEngine(options, configService, new(new Clients(handler), options), NullLogger<IntegrationEngine>.Instance, runtime);
            return new(configService, runtime, source, engine, options, clock, NullLogger<IntegrationMonitoringService>.Instance);
        }
        await Task.WhenAll(Worker().TickAsync(default), Worker().TickAsync(default));
        Assert.Equal(1, handler.Count);
        Assert.DoesNotContain("Invoices.Run", store.Runtime!.ProtectedConfiguration);
        await Worker().TickAsync(default); Assert.Equal(1, handler.Count);
        clock.UtcNow = Now.AddMinutes(1); source.Healthy = true;
        await Task.WhenAll(Worker().TickAsync(default), Worker().TickAsync(default));
        Assert.Equal(2, handler.Count);
        var state = (await new MonitoringStateStore(store, protection).ReadAsync(default)).State;
        Assert.Empty(state.Deliveries); Assert.Equal(2, state.History.Count);
        Assert.Equal("healthy", Assert.Single(state.Monitors).View.Status);
    }

    [Fact]
    public async Task FailedDelivery_IsRetriedAfterRestart_AndOldLeaseCannotClaimItEarly()
    {
        var store = new AtomicStore(); var protection = new EphemeralDataProtectionProvider(); var options = new IntegrationOptions();
        var configuration = new IntegrationConfigurationService(store, protection, options, [new WebhookFormatter()]);
        await configuration.SaveAsync(Config(), default);
        var clock = new Clock(); var handler = new Handler { Status = HttpStatusCode.BadRequest };
        var stateStore = new MonitoringStateStore(store, protection);
        var engine = new IntegrationEngine(options, configuration, new(new Clients(handler), options), NullLogger<IntegrationEngine>.Instance);
        var service = new IntegrationMonitoringService(configuration, stateStore, new Source(clock), engine, options, clock, NullLogger<IntegrationMonitoringService>.Instance);
        await service.TickAsync(default); Assert.Equal(1, handler.Count);
        await service.TickAsync(default); Assert.Equal(1, handler.Count);
        clock.UtcNow = Now.AddMinutes(1); handler.Status = HttpStatusCode.OK;
        await service.TickAsync(default); Assert.Equal(2, handler.Count);
        Assert.Empty((await stateStore.ReadAsync(default)).State.Deliveries);
    }

    [Theory]
    [InlineData("bad-zone", "08:00", 3)]
    [InlineData("UTC", "25:00", 3)]
    [InlineData("UTC", "08:00", 21)]
    public async Task Configuration_RejectsInvalidMonitoringRules(string zone, string deadline, int failures)
    {
        var config = Config(); config.Monitors[0].TimeZoneId = zone; config.Monitors[0].DeadlineLocalTime = deadline; config.Monitors[0].FailureThreshold = failures;
        var service = new IntegrationConfigurationService(new AtomicStore(), new EphemeralDataProtectionProvider(), new(), [new WebhookFormatter()]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(config, default));
    }

    [Fact]
    public async Task ExecutionSource_CombinesTimeAndCron_ExcludesDispatchers_AndIncludesChainSteps()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(o => o.UseDefinedCronApplicationNamespace("monitor-source-" + Guid.NewGuid()).UseReconciliationEpoch(1).AddIntegrations());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scheduler = container.GetRequiredService<SchedulerOptionsBuilder>();
        await provider.BeginReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        await provider.CommitReconciliationActivationEpochAsync(scheduler.RuntimeActivationScope, scheduler.ReconciliationEpoch);
        var rule = Config().Monitors[0];
        var child = new TimeTickerEntity { Id = Guid.NewGuid(), Function = rule.Function, Status = TickerStatus.InProgress };
        var completed = new TimeTickerEntity { Id = Guid.NewGuid(), Function = rule.Function, Status = TickerStatus.Done, ExecutedAt = Now.AddMinutes(-2), Children = [child] };
        var failed = new TimeTickerEntity { Id = Guid.NewGuid(), Function = rule.Function, Status = TickerStatus.Failed, ExecutedAt = Now.AddMinutes(-1) };
        var future = new TimeTickerEntity { Id = Guid.NewGuid(), Function = rule.Function, Status = TickerStatus.Done, ExecutedAt = Now.AddMinutes(1) };
        var skipped = new TimeTickerEntity { Id = Guid.NewGuid(), Function = rule.Function, Status = TickerStatus.Skipped, ExecutedAt = Now };
        Assert.Equal(5, await provider.AddTimeTickers([completed, failed, future, skipped]));
        var cron = new CronTickerEntity { Id = Guid.NewGuid(), Function = rule.Function, Expression = "0 * * * * *" };
        var other = new CronTickerEntity { Id = Guid.NewGuid(), Function = "Other", Expression = "0 * * * * *" };
        await provider.InsertCronTickers([cron, other], default);
        var cronFailure = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Failed, ExecutionTime = Now.AddSeconds(-30), ExecutedAt = Now.AddSeconds(-30) };
        await provider.InsertCronTickerOccurrences([
            cronFailure,
            new() { Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Done, ExecutionTime = Now, ExecutedAt = Now, ChainRootId = completed.Id },
            new() { Id = Guid.NewGuid(), CronTickerId = other.Id, Status = TickerStatus.Done, ExecutionTime = Now, ExecutedAt = Now }
        ], default);
        var observation = await container.GetRequiredService<IMonitoringExecutionSource>().ReadAsync(rule, Now, default);
        Assert.Null(observation.Error);
        Assert.Equal(completed.ExecutedAt, observation.LastSuccessUtc);
        Assert.Equal(new[] { completed.Id, failed.Id, cronFailure.Id }, observation.Recent.Select(x => x.Id));
        Assert.Equal(child.Id, Assert.Single(observation.Running).Id);
    }

    [Fact]
    public async Task ExpiredDeliveryLease_IsReclaimed_ButLiveLeaseIsRespected()
    {
        var store = new AtomicStore(); var protection = new EphemeralDataProtectionProvider(); var options = new IntegrationOptions();
        var configuration = new IntegrationConfigurationService(store, protection, options, [new WebhookFormatter()]);
        await configuration.SaveAsync(Config(), default);
        var clock = new Clock(); var handler = new Handler(); var stateStore = new MonitoringStateStore(store, protection);
        var engine = new IntegrationEngine(options, configuration, new(new Clients(handler), options), NullLogger<IntegrationEngine>.Instance);
        var service = new IntegrationMonitoringService(configuration, stateStore, new Source(clock), engine, options, clock, NullLogger<IntegrationMonitoringService>.Instance);
        await service.TickAsync(default, deliver: false);
        await stateStore.ChangeAsync(state => { state.Deliveries[0].Lease = Guid.NewGuid(); state.Deliveries[0].LeaseUntilUtc = Now.AddMinutes(10); return true; }, default);
        await service.DeliverPendingAsync(default); Assert.Equal(0, handler.Count);
        clock.UtcNow = Now.AddMinutes(11);
        await service.DeliverPendingAsync(default); Assert.Equal(1, handler.Count);
        Assert.Empty((await stateStore.ReadAsync(default)).State.Deliveries);
    }

    [Fact]
    public void CustomPayload_HasStableMonitorAndIncidentIdentifiers()
    {
        var config = Config(); var state = new MonitoringState(); Apply(state, config, Failure(Now));
        var message = state.Deliveries[0].Message;
        var payload = PayloadTemplates.Render("""{"monitor":"{{monitorId}}","incident":"{{incidentId}}","name":"{{monitorName}}"}""", message);
        Assert.Equal(message.IncidentId.ToString(), payload["incident"]!.GetValue<string>());
        Assert.Equal("Invoices", payload["name"]!.GetValue<string>());
    }

    [Fact]
    public void MonitoringMessages_IncludeObservedValues_AndSurviveOutboxSerialization()
    {
        var config = Config("long_running"); config.Monitors[0].DurationSeconds = 30;
        var state = new MonitoringState(); var job = Guid.NewGuid();
        Apply(state, config, new(Now, null, [], [new(job, null)]));
        Apply(state, config, new(Now.AddSeconds(95), null, [], [new(job, null)]));
        var encoded = JsonSerializer.Serialize(state, IntegrationJsonContext.Default.MonitoringState);
        var restored = JsonSerializer.Deserialize(encoded, IntegrationJsonContext.Default.MonitoringState)!;
        var message = Assert.Single(restored.Deliveries).Message;
        Assert.Equal("long_running", message.MonitorKind);
        Assert.Equal(30, message.ThresholdSeconds);
        Assert.Equal(95, message.ObservedDurationSeconds);
        Assert.Null(message.FailureCount);
        Apply(restored, config, new(Now.AddMinutes(2), Now.AddMinutes(2), [new(job, TickerStatus.Done, Now.AddMinutes(2))], []));
        var recovery = restored.Deliveries[1].Message;
        Assert.Equal(message.IncidentId, recovery.IncidentId);
        Assert.Equal(30, recovery.ThresholdSeconds);
        Assert.Null(recovery.ObservedDurationSeconds);
    }

    private sealed class Clock : ITickerClock { public DateTime UtcNow { get; set; } = Now; }
    private sealed class Source(Clock clock) : IMonitoringExecutionSource
    {
        public bool Healthy;
        public Task<MonitoringObservation> ReadAsync(IntegrationMonitor rule, DateTime now, CancellationToken ct, DateTime? afterUtc = null)
            => Task.FromResult(Healthy ? new MonitoringObservation(now, now, [new(Guid.NewGuid(), TickerStatus.Done, now)], []) : Failure(clock.UtcNow));
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Count; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Count); await Task.Delay(10, ct); return new(Status); }
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class AtomicStore : ITickerIntegrationSettingsStore
    {
        private readonly object _gate = new(); private IntegrationSettingsRecord? _settings; public IntegrationSettingsRecord? Runtime;
        public bool SupportsIntegrationMonitoring => true;
        public Task<IntegrationSettingsRecord?> ReadIntegrationSettingsAsync(CancellationToken cancellationToken = default) { lock (_gate) return Task.FromResult(_settings); }
        public Task<IntegrationSettingsRecord?> ReadIntegrationMonitoringAsync(CancellationToken cancellationToken = default) { lock (_gate) return Task.FromResult(Runtime); }
        public Task<bool> SaveIntegrationSettingsAsync(IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        { lock (_gate) { if ((_settings?.Version ?? Guid.Empty) != expectedVersion) return Task.FromResult(false); _settings = record; return Task.FromResult(true); } }
        public Task<bool> SaveIntegrationMonitoringAsync(IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        { lock (_gate) { if ((Runtime?.Version ?? Guid.Empty) != expectedVersion) return Task.FromResult(false); Runtime = record; return Task.FromResult(true); } }
    }
}
