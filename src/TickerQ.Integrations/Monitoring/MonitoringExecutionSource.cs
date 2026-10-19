using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Monitoring;

internal sealed record MonitorExecution(Guid Id, TickerStatus Status, DateTime? CompletedUtc,
    long ElapsedMilliseconds = 0, Guid? Generation = null);
internal sealed record MonitorRunning(Guid Id, Guid? Generation);
internal sealed record MonitoringObservation(DateTime AtUtc, DateTime? LastSuccessUtc,
    MonitorExecution[] Recent, MonitorRunning[] Running, string? Error = null)
{
    public DateTime? ThroughUtc { get; init; }
    public bool HasMore { get; init; }
}
internal interface IMonitoringExecutionSource
{
    Task<MonitoringObservation> ReadAsync(IntegrationMonitor rule, DateTime now, CancellationToken ct, DateTime? afterUtc = null);
}

internal sealed class MonitoringExecutionSource<TTimeTicker, TCronTicker>(ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
    : IMonitoringExecutionSource
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    internal const int PageSize = TickerMonitoringSnapshot.PageSize;

    public async Task<MonitoringObservation> ReadAsync(IntegrationMonitor rule, DateTime now, CancellationToken ct, DateTime? afterUtc = null)
    {
        var since = afterUtc ?? now.AddHours(-Math.Max(72, rule.LookbackHours));
        var page = await provider.ReadMonitoringSnapshotAsync(rule.Function, since, now, ct).ConfigureAwait(false);
        if (page.Error != null) return new(now, null, [], [], page.Error);
        var recent = page.Completed.Select(x => new MonitorExecution(x.Id, x.Status, x.CompletedUtc, x.ElapsedMilliseconds, x.Generation))
            .OrderBy(x => x.CompletedUtc).ToArray();
        var more = recent.Length > PageSize;
        // Never split equal timestamps: database GUID ordering differs by provider.
        // A partial timestamp group is retried whole on the next page.
        var through = more ? recent[PageSize].CompletedUtc!.Value.AddTicks(-1) : now;
        recent = recent.Where(x => x.CompletedUtc <= through).ToArray();
        if (more && recent.Length == 0)
            return new(now, null, [], [], $"More than {PageSize} completions share one timestamp; monitoring cannot safely advance.");
        var running = more ? [] : page.Running;
        if (running.Length > TickerMonitoringSnapshot.MaxRunning) return new(now, null, [], [], "More than 1,000 active executions match this function.");
        var success = recent.Where(x => x.Status is TickerStatus.Done or TickerStatus.DueDone).Select(x => x.CompletedUtc).DefaultIfEmpty().Max();
        return new(now, success, recent, running.Select(x => new MonitorRunning(x.Id, x.Generation)).DistinctBy(x => x.Id).ToArray())
        { ThroughUtc = through, HasMore = more };
    }
}
