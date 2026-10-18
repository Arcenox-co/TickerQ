using System.Globalization;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Infrastructure;

internal static class MessageContent
{
    internal static readonly string[] Categories = ["job_alert", "monitoring_alert", "recovery"];
    internal static string Category(IntegrationMessage m) => m.Event.Kind == "monitoring_recovered" ? "recovery"
        : m.MonitorId != null || m.Event.Kind.StartsWith("monitoring_", StringComparison.Ordinal) ? "monitoring_alert" : "job_alert";

    internal static string? Template(IntegrationDestination destination, IntegrationMessage message)
        => destination.MessageTemplates is not { } templates ? destination.PayloadTemplate : Category(message) switch
        {
            "monitoring_alert" => templates.MonitoringAlert,
            "recovery" => templates.Recovery,
            _ => templates.JobAlert
        };

    internal static Dictionary<string, string> Values(IntegrationMessage m)
    {
        var kind = m.MonitorKind ?? (m.Event.Kind.StartsWith("monitoring_", StringComparison.Ordinal) ? m.Event.Kind[11..] : "");
        var recovery = Category(m) == "recovery";
        var title = m.Event.Kind switch
        {
            "failed" => "Job failed",
            "timeout_cancelled" => "Job timed out",
            "contract_drift" => "Job request needs attention",
            "stale_recovered" => "Abandoned jobs recovered",
            "monitoring_deadline" => "Daily deadline missed",
            "monitoring_long_running" => "Execution duration exceeded",
            "monitoring_consecutive_failures" => "Repeated job failures",
            "monitoring_recovered" => "Monitoring recovered",
            _ => "Job alert"
        };
        var severity = recovery ? "success" : m.Event.Kind is "failed" or "monitoring_consecutive_failures" ? "error" : "warning";
        var threshold = kind switch
        {
            "long_running" when m.ThresholdSeconds != null => $"{m.ThresholdSeconds} seconds",
            "consecutive_failures" when m.FailureThreshold != null => $"{m.FailureThreshold} final failures",
            "deadline" when m.DeadlineUtc != null => m.DeadlineUtc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            _ => ""
        };
        var observed = recovery ? "Successful execution confirmed" : kind switch
        {
            "long_running" when m.ObservedDurationSeconds != null => $"At least {m.ObservedDurationSeconds} seconds observed",
            "consecutive_failures" when m.FailureCount != null => $"At least {m.FailureCount} final failures",
            "deadline" => "No successful execution in the daily window",
            _ => ""
        };
        return new(StringComparer.Ordinal)
        {
            ["title"] = title, ["severity"] = severity,
            ["statusIcon"] = recovery ? "✅" : severity == "error" ? "🔴" : "⚠️",
            ["statusColor"] = recovery ? "Good" : severity == "error" ? "Attention" : "Warning",
            ["condition"] = kind switch { "deadline" => "Daily deadline", "long_running" => "Long-running execution", "consecutive_failures" => "Repeated failures", _ => "" },
            ["threshold"] = threshold, ["observed"] = observed,
            ["monitorKind"] = m.MonitorKind ?? "", ["thresholdSeconds"] = Number(m.ThresholdSeconds),
            ["observedDurationSeconds"] = Number(m.ObservedDurationSeconds), ["failureCount"] = Number(m.FailureCount),
            ["failureThreshold"] = Number(m.FailureThreshold), ["deadlineUtc"] = m.DeadlineUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["timeZoneId"] = m.TimeZoneId ?? "", ["graceMinutes"] = Number(m.GraceMinutes),
            ["occurredAt"] = m.Event.OccurredAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        };
    }
    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
