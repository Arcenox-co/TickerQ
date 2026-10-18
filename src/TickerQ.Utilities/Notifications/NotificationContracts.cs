#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Notifications;

/// <summary>Encrypted configuration; the persistence provider supplies the runtime namespace.</summary>
public sealed record NotificationSettingsRecord(Guid Version, string ProtectedConfiguration);

public interface ITickerNotificationSettingsStore
{
    bool NotificationSettingsArePersistent => false;
    Task<NotificationSettingsRecord?> ReadNotificationSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromException<NotificationSettingsRecord?>(new NotSupportedException("This operational store does not support notification settings."));
    /// <summary>Compare-and-swap. Guid.Empty means that no settings exist yet.</summary>
    Task<bool> SaveNotificationSettingsAsync(NotificationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        => Task.FromException<bool>(new NotSupportedException("This operational store does not support notification settings."));
}

public sealed class NotificationDestination
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "webhook";
    public bool Enabled { get; set; } = true;
    /// <summary>Write-only through dashboard APIs. Null preserves the saved URL.</summary>
    public string? Url { get; set; }
    /// <summary>Write-only. Null preserves saved headers; an empty dictionary removes them.</summary>
    public Dictionary<string, string>? Headers { get; set; }
    public string? PayloadTemplate { get; set; }
    public string? EndpointHost { get; set; }
    public bool HasUrl { get; set; }
    public bool HasHeaders { get; set; }
}

public sealed class NotificationRule
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

public sealed class NotificationConfiguration
{
    public Guid Version { get; set; }
    public List<NotificationDestination> Destinations { get; set; } = [];
    public List<NotificationRule> Rules { get; set; } = [];
}

public sealed class NotificationDeliveryStatus
{
    public Guid DestinationId { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Outcome { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class NotificationSnapshot
{
    public bool Available { get; set; }
    public bool CanManage { get; set; }
    public bool Persistent { get; set; }
    public NotificationConfiguration Configuration { get; set; } = new();
    public List<string> Providers { get; set; } = [];
    public List<string> TemplateTokens { get; set; } = [];
    public List<NotificationDeliveryStatus> RecentDeliveries { get; set; } = [];
    public long Dropped { get; set; }
    public long Suppressed { get; set; }
}

public sealed class NotificationPreviewRequest
{
    public string Provider { get; set; } = "webhook";
    public string? PayloadTemplate { get; set; }
}

public sealed class NotificationPreviewResponse
{
    public string Payload { get; set; } = "";
}

/// <summary>Optional module bridge: the dashboard has no dependency on the Notifications package.</summary>
public interface ITickerNotificationAdminService
{
    Task<NotificationSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(NotificationConfiguration configuration, CancellationToken cancellationToken = default);
    Task<NotificationDeliveryStatus> TestAsync(Guid destinationId, CancellationToken cancellationToken = default);
    NotificationPreviewResponse Preview(NotificationPreviewRequest request);
}

public sealed class NotificationConfigurationConflictException : Exception
{
    public NotificationConfigurationConflictException() : base("Notification settings changed. Reload before saving your changes.") { }
}
