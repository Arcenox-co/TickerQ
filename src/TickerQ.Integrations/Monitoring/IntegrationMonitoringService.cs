using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Integrations.Monitoring;

internal sealed class IntegrationMonitoringService(IntegrationConfigurationService configuration,
    MonitoringStateStore store, IMonitoringExecutionSource executions, IntegrationEngine engine,
    IntegrationOptions options, ITickerClock clock, ILogger<IntegrationMonitoringService> logger) : BackgroundService
{
    private bool _catchingUp;
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => !store.Available ? Task.CompletedTask : Task.WhenAll(
            RunLoopAsync(ct => TickAsync(ct, deliver: false), () => _catchingUp ? TimeSpan.FromSeconds(1) : options.MonitoringInterval, stoppingToken),
            RunLoopAsync(DeliverPendingAsync, () => TimeSpan.FromSeconds(5), stoppingToken));

    private async Task RunLoopAsync(Func<CancellationToken, Task> action, Func<TimeSpan> interval, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await action(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Integration monitoring operation failed ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(interval(), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task TickAsync(CancellationToken ct, bool deliver = true)
    {
        _catchingUp = false;
        var config = await configuration.LoadAsync(ct).ConfigureAwait(false);
        // Avoid even creating runtime state when this feature has never been configured.
        if (config.Monitors.Count == 0 && (await store.ReadAsync(ct).ConfigureAwait(false)).Version == Guid.Empty) return;
        var lease = Guid.NewGuid();
        var acquired = await store.ChangeAsync(state =>
        {
            var now = clock.UtcNow;
            if (state.ScanLeaseUntilUtc > now) return false;
            state.ScanLease = lease; state.ScanLeaseUntilUtc = now.AddMinutes(1);
            return true;
        }, ct).ConfigureAwait(false);
        if (acquired)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(40));
                var observations = new Dictionary<Guid, MonitoringObservation>();
                var saved = (await store.ReadAsync(ct).ConfigureAwait(false)).State;
                // Conditions on the same function share one snapshot. Read the longest
                // requested lookback so a deadline/duration rule cannot narrow a failure rule.
                foreach (var group in config.Monitors.Where(x => x.Enabled).GroupBy(x => x.Function, StringComparer.Ordinal))
                {
                    var ready = group.Where(rule => !saved.Monitors.Any(s => s.Id == rule.Id
                        && s.Fingerprint == MonitorEvaluation.Fingerprint(rule) && s.PendingTransitions.Count > 0)).ToArray();
                    if (ready.Length == 0) continue;
                    var checkpoints = ready.Select(rule => saved.Monitors.FirstOrDefault(s => s.Id == rule.Id
                        && s.Fingerprint == MonitorEvaluation.Fingerprint(rule))?.ProcessedThroughUtc).ToArray();
                    var after = checkpoints.Any(x => x == null) ? null : checkpoints.Min();
                    var now = clock.UtcNow;
                    MonitoringObservation observation;
                    try { observation = await executions.ReadAsync(ready.MaxBy(x => x.LookbackHours)!, now, timeout.Token, after).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Monitoring function data is unavailable ({ErrorType}).", ex.GetType().Name);
                        observation = new(now, null, [], [], "Execution data is unavailable; this check cannot confirm health.");
                    }
                    _catchingUp |= observation.HasMore;
                    foreach (var rule in ready) observations[rule.Id] = observation;
                }
                // Never publish observations against a configuration edited during the scan.
                if ((await configuration.LoadAsync(ct).ConfigureAwait(false)).Version == config.Version)
                    await store.ChangeAsync(state =>
                    {
                        var now = clock.UtcNow;
                        if (state.ScanLease != lease || state.ScanLeaseUntilUtc <= now) return false;
                        ApplyObservations(state, config, observations, options, now);
                        state.ScanLeaseUntilUtc = default; state.ScanLease = default;
                        return true;
                    }, ct).ConfigureAwait(false);
            }
            finally
            {
                // A cancelled host leaves an expiring lease, never an indefinite lock.
                if (!ct.IsCancellationRequested)
                    await store.ChangeAsync(state =>
                    {
                        if (state.ScanLease != lease) return false;
                        state.ScanLeaseUntilUtc = default; state.ScanLease = default; return true;
                    }, ct).ConfigureAwait(false);
            }
        }
        if (deliver) await DeliverPendingAsync(ct).ConfigureAwait(false);
    }

    internal static void ApplyObservations(MonitoringState state, IntegrationConfiguration config,
        IReadOnlyDictionary<Guid, MonitoringObservation> observations, IntegrationOptions options, DateTime now)
    {
        state.Monitors.RemoveAll(s => config.Monitors.All(r => r.Id != s.Id));
        state.History.RemoveAll(x => x.AtUtc < now.AddDays(-7));
        if (state.History.Count > 50) state.History.RemoveRange(0, state.History.Count - 50);
        state.Deliveries.RemoveAll(d => config.Monitors.All(r => r.Id != d.MonitorId || !r.Enabled || MonitorEvaluation.Fingerprint(r) != d.Fingerprint)
            || config.Destinations.All(x => x.Id != d.DestinationId || !x.Enabled));
        DrainTransitions(state, config, now);
        foreach (var rule in config.Monitors)
        {
            var fingerprint = MonitorEvaluation.Fingerprint(rule);
            var current = state.Monitors.SingleOrDefault(x => x.Id == rule.Id);
            if (current == null || current.Fingerprint != fingerprint)
            {
                state.Monitors.RemoveAll(x => x.Id == rule.Id);
                current = new() { Id = rule.Id, Fingerprint = fingerprint, InitializedUtc = now, View = new() { MonitorId = rule.Id } };
                state.Monitors.Add(current);
            }
            if (!rule.Enabled) { current.View.Status = "paused"; current.View.Message = "This monitor is paused."; continue; }
            if (current.PendingTransitions.Count > 0) continue;
            if (!observations.TryGetValue(rule.Id, out var observation)) continue;
            MonitorEvaluation.Process(rule, current, observation,
                (decision, at) => ApplyDecision(current, rule, config, decision, options, at, observation.Error != null));
            current.View.LastCheckedUtc = now;
            DrainTransitions(state, config, now);
        }
    }

    private static void ApplyDecision(MonitorState current, IntegrationMonitor rule, IntegrationConfiguration config,
        MonitorDecision decision, IntegrationOptions options, DateTime at, bool error)
    {
        current.View.Message = decision.Message;
        if (decision.Breached == null)
        {
            current.View.Status = current.IncidentId != null ? "incident" : error ? "unknown" : "waiting";
            return;
        }
        var opening = decision.Breached.Value && current.IncidentId == null;
        var closing = !decision.Breached.Value && current.IncidentId != null;
        var destinations = config.Destinations.Where(x => x.Enabled && rule.DestinationIds.Contains(x.Id)).ToArray();
        var incident = current.IncidentId ?? Guid.NewGuid();
        if (opening || closing && rule.NotifyRecovery)
        {
            var kind = opening ? "monitoring_" + rule.Kind : "monitoring_recovered";
            var message = new IntegrationMessage(Guid.NewGuid(), options.ApplicationName, options.EnvironmentName,
                new TickerFailureEvent { Kind = kind, Function = rule.Function, Reason = decision.Message,
                    TickerId = decision.TickerId, TickerType = "FunctionMonitor", Node = "monitoring", OccurredAtUtc = at }, options.DashboardUrl ?? "")
                {
                    MonitorId = rule.Id, MonitorName = rule.Name, IncidentId = incident, MonitorKind = rule.Kind,
                    ThresholdSeconds = rule.Kind == "long_running" ? rule.DurationSeconds : null,
                    ObservedDurationSeconds = decision.ObservedDurationSeconds,
                    FailureCount = decision.FailureCount,
                    FailureThreshold = rule.Kind == "consecutive_failures" ? rule.FailureThreshold : null,
                    DeadlineUtc = decision.DeadlineUtc,
                    TimeZoneId = rule.Kind == "deadline" ? rule.TimeZoneId : null,
                    GraceMinutes = rule.Kind == "deadline" ? rule.GraceMinutes : null
                };
            if (destinations.Length > 0)
                current.PendingTransitions.Add(new() { Message = message, DestinationIds = destinations.Select(x => x.Id).ToList() });
        }
        if (opening) { current.IncidentId = incident; current.View.OpenedAtUtc = at; current.View.RecoveredAtUtc = null; }
        if (closing) { current.IncidentId = null; current.View.RecoveredAtUtc = at; }
        current.View.Status = current.IncidentId != null ? "incident" : "healthy";
    }

    // Stored transitions are compact (one message plus remaining destination IDs),
    // independent of the bounded HTTP delivery queue. Both move under the same CAS.
    private static bool DrainTransitions(MonitoringState state, IntegrationConfiguration config, DateTime now)
    {
        var changed = false;
        foreach (var monitor in state.Monitors)
        {
            var rule = config.Monitors.FirstOrDefault(x => x.Id == monitor.Id);
            if (rule == null || !rule.Enabled || MonitorEvaluation.Fingerprint(rule) != monitor.Fingerprint)
            {
                changed |= monitor.PendingTransitions.Count > 0;
                monitor.PendingTransitions.Clear();
                continue;
            }
            foreach (var pending in monitor.PendingTransitions.ToArray())
            {
                changed |= pending.DestinationIds.RemoveAll(id => config.Destinations.All(x => x.Id != id || !x.Enabled)) > 0;
                while (pending.DestinationIds.Count > 0 && state.Deliveries.Count < 256)
                {
                    state.Deliveries.Add(new() { MonitorId = monitor.Id, Fingerprint = monitor.Fingerprint,
                        DestinationId = pending.DestinationIds[0], Message = pending.Message, NextAttemptUtc = now });
                    pending.DestinationIds.RemoveAt(0);
                    changed = true;
                }
                if (pending.DestinationIds.Count > 0) break;
                monitor.PendingTransitions.Remove(pending);
                changed = true;
            }
        }
        return changed;
    }

    internal async Task DeliverPendingAsync(CancellationToken ct)
    {
        // A bounded batch prevents a failing receiver from monopolizing the service.
        for (var i = 0; i < 4; i++)
        {
            var config = await configuration.LoadAsync(ct).ConfigureAwait(false);
            if (!config.Monitors.Any(x => x.Enabled)) return;
            MonitorDelivery? claimed = null;
            var token = Guid.NewGuid();
            var acquired = await store.ChangeAsync(state =>
            {
                claimed = null;
                var now = clock.UtcNow;
                var drained = DrainTransitions(state, config, now);
                foreach (var delivery in state.Deliveries)
                {
                    if (delivery.NextAttemptUtc > now || delivery.LeaseUntilUtc > now) continue;
                    var rule = config.Monitors.SingleOrDefault(x => x.Id == delivery.MonitorId);
                    if (rule == null || !rule.Enabled || MonitorEvaluation.Fingerprint(rule) != delivery.Fingerprint || config.Destinations.All(x => x.Id != delivery.DestinationId || !x.Enabled)) continue;
                    // Preserve alert/recovery order for each monitor and destination.
                    if (state.Deliveries.TakeWhile(x => x.Id != delivery.Id).Any(x => x.MonitorId == delivery.MonitorId && x.DestinationId == delivery.DestinationId)) continue;
                    delivery.Lease = token; delivery.LeaseUntilUtc = now.AddMinutes(10); delivery.Attempts++;
                    claimed = delivery; return true;
                }
                return drained;
            }, ct).ConfigureAwait(false);
            if (!acquired || claimed == null) return;
            var destination = config.Destinations.Single(x => x.Id == claimed.DestinationId);
            var result = await engine.SendMonitorAsync(destination, claimed.Message, ct).ConfigureAwait(false);
            await store.ChangeAsync(state =>
            {
                var delivery = state.Deliveries.SingleOrDefault(x => x.Id == claimed.Id && x.Lease == token);
                if (delivery == null) return false;
                state.History.Add(new() { DestinationId = destination.Id, AtUtc = clock.UtcNow, Outcome = result.Outcome,
                    Message = $"{claimed.Message.MonitorName}: {result.Message}" });
                if (state.History.Count > 50) state.History.RemoveAt(0);
                if (result.Outcome == "Sent" || delivery.Attempts >= 3) state.Deliveries.Remove(delivery);
                else { delivery.Lease = null; delivery.LeaseUntilUtc = default; delivery.NextAttemptUtc = clock.UtcNow.AddMinutes(delivery.Attempts); }
                return true;
            }, ct).ConfigureAwait(false);
        }
    }
}
