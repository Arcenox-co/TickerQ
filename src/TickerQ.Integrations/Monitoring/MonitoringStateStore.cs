using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TickerQ.Utilities.Integrations;
using TickerQ.Integrations.Infrastructure;

namespace TickerQ.Integrations.Monitoring;

internal sealed class MonitoringState
{
    public Guid ScanLease { get; set; }
    public DateTime ScanLeaseUntilUtc { get; set; }
    public List<MonitorState> Monitors { get; set; } = [];
    public List<MonitorDelivery> Deliveries { get; set; } = [];
    public List<IntegrationDeliveryStatus> History { get; set; } = [];
}

internal sealed class MonitorState
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTime InitializedUtc { get; set; }
    public Guid? IncidentId { get; set; }
    public IntegrationMonitorStatus View { get; set; } = new();
    public Dictionary<Guid, ObservedRun> RunningSince { get; set; } = [];
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? ProcessedThroughUtc { get; set; }
    public DateTime? ReplayFromUtc { get; set; }
    public List<MonitorExecution> SeenCompletions { get; set; } = [];
    public bool ReplayTrackingInitialized { get; set; }
    public DateTime? LastStreakBreakUtc { get; set; }
    public DateTime? LastDeadlineUtc { get; set; }
    public List<DateTime> ConsecutiveFailures { get; set; } = [];
    // One bounded observation batch per monitor. Drain it before observing another
    // batch, so a full delivery queue cannot erase an already detected transition.
    public List<MonitorTransition> PendingTransitions { get; set; } = [];
}

internal sealed class MonitorTransition
{
    public IntegrationMessage Message { get; set; } = null!;
    public List<Guid> DestinationIds { get; set; } = [];
}

internal sealed class ObservedRun
{
    public DateTime SinceUtc { get; set; }
    public Guid? Generation { get; set; }
}

internal sealed class MonitorDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MonitorId { get; set; }
    public string Fingerprint { get; set; } = "";
    public Guid DestinationId { get; set; }
    public IntegrationMessage Message { get; set; } = null!;
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public Guid? Lease { get; set; }
    public DateTime LeaseUntilUtc { get; set; }
}

internal sealed class MonitoringStateStore(ITickerIntegrationSettingsStore store, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("TickerQ.Integrations.Monitoring.v1");
    internal bool Available => store.SupportsIntegrationMonitoring;
    internal async Task<(Guid Version, MonitoringState State)> ReadAsync(CancellationToken ct)
    {
        var row = await store.ReadIntegrationMonitoringAsync(ct).ConfigureAwait(false);
        return row == null ? (Guid.Empty, new()) : (row.Version,
            JsonSerializer.Deserialize(_protector.Unprotect(row.ProtectedConfiguration), IntegrationJsonContext.Default.MonitoringState)
            ?? throw new InvalidOperationException("Invalid monitoring state."));
    }

    // Each callback runs on a fresh, private copy. A loser retries without publishing side effects.
    internal async Task<bool> ChangeAsync(Func<MonitoringState, bool> change, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (version, state) = await ReadAsync(ct).ConfigureAwait(false);
            if (!change(state)) return false;
            var payload = _protector.Protect(JsonSerializer.Serialize(state, IntegrationJsonContext.Default.MonitoringState));
            if (await store.SaveIntegrationMonitoringAsync(new(Guid.NewGuid(), payload), version, ct).ConfigureAwait(false)) return true;
        }
        return false;
    }
}
