using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Dashboard.Assistant;

internal static partial class TickerAssistantTools
{
    // Reads are available to dashboard viewers; proposals require integration-management permission.
    // Never serialize the service snapshot into model context: templates, URLs and headers may contain secrets.
    private static void AddIntegrationTools<TTimeTicker, TCronTicker>(IList<AITool> tools,
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data, ITickerIntegrationAdminService service,
        bool allowProposals, Func<AssistantProposal, Task>? emit, CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tools.Add(AIFunctionFactory.Create(async () =>
        {
            var s = await service.GetAsync(ct);
            var sb = new StringBuilder($"Persistent={s.Persistent}; monitoring available={s.MonitoringAvailable}; check interval={s.MonitoringCheckIntervalSeconds}s; pending deliveries={s.MonitoringPendingDeliveries}; dropped={s.Dropped}; suppressed={s.Suppressed}.\nDestinations:\n");
            foreach (var d in s.Configuration.Destinations.Take(32))
                sb.AppendLine($"{Trim(d.Name)} id={d.Id} provider={d.Provider} enabled={d.Enabled}");
            sb.AppendLine("Alert rules:");
            foreach (var r in s.Configuration.Rules.Take(50))
                sb.AppendLine($"{Trim(r.Name)} id={r.Id} enabled={r.Enabled} events={string.Join(',', r.Events)} include={string.Join(',', r.IncludeFunctions)} exclude={string.Join(',', r.ExcludeFunctions)} cooldown={r.CooldownSeconds}s destinations={string.Join(',', r.DestinationIds)}");
            sb.AppendLine("Monitors (individual function executions, including chain steps):");
            foreach (var m in s.Configuration.Monitors.Take(20))
            {
                var state = s.Monitors.FirstOrDefault(x => x.MonitorId == m.Id);
                sb.AppendLine($"{Trim(m.Name)} id={m.Id} enabled={m.Enabled} function={m.Function} kind={m.Kind} deadline={m.DeadlineLocalTime} zone={m.TimeZoneId} grace={m.GraceMinutes}min duration={m.DurationSeconds}s failures={m.FailureThreshold} lookback={m.LookbackHours}h recovery={m.NotifyRecovery} destinations={string.Join(',', m.DestinationIds)} status={state?.Status ?? "unknown"} lastCheck={Fmt(state?.LastCheckedUtc)}");
            }
            sb.AppendLine("Recent deliveries (no response bodies):");
            foreach (var d in s.RecentDeliveries.TakeLast(20))
                sb.AppendLine($"destination={d.DestinationId} at={d.AtUtc:O} outcome={d.Outcome}");
            return sb.Length <= 24000 ? sb.ToString() : sb.ToString(0, 24000) + "\n[Output truncated]";
        }, "get_integrations", "Inspect existing destinations, alert rules, monitor states and recent delivery outcomes. Does not expose credentials, URLs, headers, templates or send messages."));

        if (!allowProposals || emit == null) return;
        tools.Add(AIFunctionFactory.Create(async (
            [Description("Short rule name, max 80 characters.")] string name,
            [Description("One exact registered function, or empty to include all functions.")] string functionName,
            [Description("Comma-separated existing destination GUIDs from get_integrations.")] string destinationIds,
            [Description("Comma-separated: failed, timeout_cancelled, stale_recovered, contract_drift.")] string events,
            [Description("Cooldown between alerts for the same destination/event/function on a node, 0–86400 seconds.")] int cooldownSeconds) =>
        {
            if (!ValidName(name)) return "Use a rule name of 1–80 characters.";
            if (!string.IsNullOrWhiteSpace(functionName))
            {
                var error = await ValidateFunctionAsync(data, functionName, ct);
                if (error != null) return error;
            }
            var selectedEvents = Split(events);
            if (selectedEvents.Count == 0 || selectedEvents.Any(x => x is not ("failed" or "timeout_cancelled" or "stale_recovered" or "contract_drift")))
                return "Choose supported events: failed, timeout_cancelled, stale_recovered, contract_drift.";
            if (cooldownSeconds is < 0 or > 86400) return "Cooldown must be 0–86400 seconds.";
            var snapshot = await service.GetAsync(ct);
            if (snapshot.Configuration.Rules.Count >= 100) return "The alert rule limit has been reached.";
            var ids = DestinationIds(snapshot, destinationIds);
            if (ids == null) return "Choose 1–32 distinct enabled destination IDs from get_integrations.";
            var rule = new IntegrationRule { Name = name.Trim(), DestinationIds = ids, Events = selectedEvents,
                IncludeFunctions = string.IsNullOrWhiteSpace(functionName) ? [] : [functionName], CooldownSeconds = cooldownSeconds };
            await emit(new AssistantProposal { Kind = "createAlertRule", Function = functionName ?? "", Rule = rule,
                ConfigurationVersion = snapshot.Configuration.Version, DestinationNames = DestinationNames(snapshot, ids),
                Warning = "Enables alerts to the selected destinations for future matching events. No test message is sent by this confirmation." });
            return ProposalShown;
        }, "propose_alert_rule", "Draft a new alert rule using existing destinations. Empty function matches ALL functions. Requires explicit user confirmation to save; never sends a test message."));

        tools.Add(AIFunctionFactory.Create(async (
            [Description("Short monitor name, max 80 characters.")] string name,
            [Description("One exact registered function name; no wildcards.")] string functionName,
            [Description("deadline, long_running, or consecutive_failures.")] string kind,
            [Description("Comma-separated existing enabled destination GUIDs.")] string destinationIds,
            [Description("Send a recovery notification when the monitored incident clears.")] bool notifyRecovery,
            [Description("Deadline local time HH:mm; use 08:00 when irrelevant.")] string deadlineLocalTime,
            [Description("Explicit deadline time zone, e.g. Europe/Berlin or UTC. Do not infer it from the browser.")] string timeZoneId,
            [Description("Deadline grace 0–720 minutes. Default 10.")] int graceMinutes,
            [Description("Long-running threshold 5–604800 seconds. Default 300.")] int durationSeconds,
            [Description("Consecutive failure threshold 2–20. Default 3.")] int failureThreshold,
            [Description("Failure lookback 1–168 hours. Default 24.")] int lookbackHours) =>
        {
            if (!ValidName(name)) return "Use a monitor name of 1–80 characters.";
            var error = await ValidateFunctionAsync(data, functionName, ct);
            if (error != null) return error;
            if (kind is not ("deadline" or "long_running" or "consecutive_failures")) return "Invalid monitor kind.";
            if (graceMinutes is < 0 or > 720 || durationSeconds is < 5 or > 604800 || failureThreshold is < 2 or > 20 || lookbackHours is < 1 or > 168)
                return "Use grace 0–720 minutes, duration 5–604800 seconds, failure threshold 2–20 and lookback 1–168 hours.";
            if (!TimeOnly.TryParseExact(deadlineLocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return "Deadline must use HH:mm.";
            try { TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return "Choose a time zone supported by this server."; }
            var snapshot = await service.GetAsync(ct);
            if (!snapshot.MonitoringAvailable) return "Monitoring is unavailable for this operational store.";
            if (snapshot.Configuration.Monitors.Count >= 20) return "The 20-monitor limit has been reached.";
            var ids = DestinationIds(snapshot, destinationIds);
            if (ids == null) return "Choose 1–32 distinct enabled destination IDs from get_integrations.";
            var monitor = new IntegrationMonitor { Name = name.Trim(), Function = functionName, Kind = kind,
                DestinationIds = ids, NotifyRecovery = notifyRecovery, DeadlineLocalTime = deadlineLocalTime,
                TimeZoneId = timeZoneId, GraceMinutes = graceMinutes, DurationSeconds = durationSeconds,
                FailureThreshold = failureThreshold, LookbackHours = lookbackHours };
            await emit(new AssistantProposal { Kind = "createMonitor", Function = functionName, Monitor = monitor,
                ConfigurationVersion = snapshot.Configuration.Version, DestinationNames = DestinationNames(snapshot, ids),
                Warning = "Monitors this function's executions, including chain steps. It does not measure completion of the whole chain. Alerts and recovery messages use the selected destinations." });
            return ProposalShown;
        }, "propose_monitor", "Draft a deadline, long-running, or consecutive-failure monitor. Uses existing destinations and requires confirmation. Ask for the deadline time zone when unclear."));
    }

    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 80;
    private static List<string> Split(string value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
    private static List<Guid>? DestinationIds(IntegrationSnapshot snapshot, string value)
    {
        var parts = Split(value);
        if (parts.Count is < 1 or > 32 || parts.Any(x => !Guid.TryParse(x, out _))) return null;
        var ids = parts.Select(Guid.Parse).ToList();
        return ids.Distinct().Count() == ids.Count && ids.All(id => snapshot.Configuration.Destinations.Any(d => d.Id == id && d.Enabled)) ? ids : null;
    }
    private static List<string> DestinationNames(IntegrationSnapshot snapshot, List<Guid> ids) =>
        ids.Select(id => snapshot.Configuration.Destinations.Single(d => d.Id == id).Name).ToList();
}
