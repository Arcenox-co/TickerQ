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
    Guid EventId, string Application, string Environment, TickerFailureEvent Event, string DashboardUrl);

/// <summary>Register another formatter to add a future application without changing the dispatcher or dashboard.</summary>
public interface IIntegrationPayloadFormatter
{
    string Name { get; }
    JsonObject Format(IntegrationDestination destination, IntegrationMessage message);
}
