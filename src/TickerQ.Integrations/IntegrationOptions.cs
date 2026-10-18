using System.Text.Json.Nodes;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations;

public sealed class IntegrationOptions
{
    public string ApplicationName { get; set; } = "TickerQ";
    public string EnvironmentName { get; set; } = "";
    /// <summary>Absolute URL including the dashboard base path. Used for notification links.</summary>
    public string? DashboardUrl { get; set; }
    /// <summary>Interval between monitoring scans. Defaults to 30 seconds; range 5 seconds to 5 minutes.</summary>
    public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int QueueCapacity { get; set; } = 1000;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Opt in only for trusted internal webhook destinations. Redirects are always disabled.</summary>
    public bool AllowPrivateEndpoints { get; set; }
    /// <summary>Optional exact DNS host allowlist; empty allows public HTTPS destinations.</summary>
    public List<string> AllowedHosts { get; set; } = [];
    /// <summary>Runs before events enter the queue. Default reason is flattened and limited to 512 characters; supply application-specific redaction here.</summary>
    public Func<string, string>? ReasonSanitizer { get; set; }
}

public sealed record IntegrationMessage(
    Guid EventId, string Application, string Environment, TickerFailureEvent Event, string DashboardUrl)
{
    public Guid? MonitorId { get; init; }
    public Guid? IncidentId { get; init; }
    public string? MonitorName { get; init; }
    public string? MonitorKind { get; init; }
    public int? ThresholdSeconds { get; init; }
    /// <summary>Persisted duration for completed runs, or time since first observed InProgress for active runs.</summary>
    public long? ObservedDurationSeconds { get; init; }
    /// <summary>Retained consecutive failures observed; can be a lower bound.</summary>
    public int? FailureCount { get; init; }
    public int? FailureThreshold { get; init; }
    public DateTime? DeadlineUtc { get; init; }
    public string? TimeZoneId { get; init; }
    public int? GraceMinutes { get; init; }
}

/// <summary>Register another formatter to add a future application without changing the dispatcher or dashboard.</summary>
public interface IIntegrationPayloadFormatter
{
    string Name { get; }
    JsonObject Format(IntegrationDestination destination, IntegrationMessage message);
}
