#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Integrations;

/// <summary>Encrypted configuration; the persistence provider supplies the runtime namespace.</summary>
public sealed record IntegrationSettingsRecord(Guid Version, string ProtectedConfiguration);

public interface ITickerIntegrationSettingsStore
{
    bool IntegrationSettingsArePersistent => false;
    Task<IntegrationSettingsRecord?> ReadIntegrationSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromException<IntegrationSettingsRecord?>(new NotSupportedException("This operational store does not support integration settings."));
    /// <summary>Compare-and-swap. Guid.Empty means that no settings exist yet.</summary>
    Task<bool> SaveIntegrationSettingsAsync(IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        => Task.FromException<bool>(new NotSupportedException("This operational store does not support integration settings."));
}

public sealed class IntegrationDestination
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "webhook";
    public bool Enabled { get; set; } = true;
    /// <summary>Write-only through dashboard APIs. Null preserves the saved URL.</summary>
    public string? Url { get; set; }
    /// <summary>Write-only. Null preserves saved headers; an empty dictionary removes them.</summary>
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>Optional JSON template for Slack, Teams, or webhooks. Null uses the provider's built-in payload.</summary>
    public string? PayloadTemplate { get; set; }
    public string? EndpointHost { get; set; }
    public bool HasUrl { get; set; }
    public bool HasHeaders { get; set; }
}

public sealed class IntegrationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<Guid> DestinationIds { get; set; } = [];
    public List<string> Events { get; set; } = ["failed", "timeout_cancelled"];
    /// <summary>Empty includes all functions. '*' and '?' wildcards are supported; matching is case-sensitive.</summary>
    public List<string> IncludeFunctions { get; set; } = [];
    public List<string> ExcludeFunctions { get; set; } = [];
    /// <summary>Per-node cooldown for the same destination, event kind, and function.</summary>
    public int CooldownSeconds { get; set; } = 60;
}

public sealed class IntegrationConfiguration
{
    public Guid Version { get; set; }
    public List<IntegrationDestination> Destinations { get; set; } = [];
    public List<IntegrationRule> Rules { get; set; } = [];
}

public sealed class IntegrationDeliveryStatus
{
    public Guid DestinationId { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Outcome { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class IntegrationSnapshot
{
    public bool Available { get; set; }
    public bool CanManage { get; set; }
    public bool Persistent { get; set; }
    public IntegrationConfiguration Configuration { get; set; } = new();
    public List<string> Providers { get; set; } = [];
    public List<string> TemplateTokens { get; set; } = [];
    public List<IntegrationDeliveryStatus> RecentDeliveries { get; set; } = [];
    public long Dropped { get; set; }
    public long Suppressed { get; set; }
}

public sealed class IntegrationPreviewRequest
{
    public string Provider { get; set; } = "webhook";
    public string? PayloadTemplate { get; set; }
}

public sealed class IntegrationPreviewResponse
{
    public string Payload { get; set; } = "";
}

/// <summary>Optional module bridge: the dashboard has no dependency on the Integrations package.</summary>
public interface ITickerIntegrationAdminService
{
    Task<IntegrationSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IntegrationConfiguration configuration, CancellationToken cancellationToken = default);
    Task<IntegrationDeliveryStatus> TestAsync(Guid destinationId, CancellationToken cancellationToken = default);
    IntegrationPreviewResponse Preview(IntegrationPreviewRequest request);
}

public sealed class IntegrationConfigurationConflictException : Exception
{
    public IntegrationConfigurationConflictException() : base("Integration settings changed. Reload before saving your changes.") { }
}
