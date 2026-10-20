using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Monitoring;

internal sealed record MonitorExecution(Guid Id, TickerStatus Status, DateTime? CompletedUtc,
    long ElapsedMilliseconds = 0, Guid? Generation = null)
{
    public bool IsCron { get; init; }
}
internal sealed record MonitorRunning(Guid Id, Guid? Generation);
internal sealed record MonitoringObservation(DateTime AtUtc, DateTime? LastSuccessUtc,
    MonitorExecution[] Recent, MonitorRunning[] Running, string? Error = null)
{
    public DateTime? ThroughUtc { get; init; }
    public bool HasMore { get; init; }
    public MonitorExecution[] Replayed { get; init; } = [];
}
internal interface IMonitoringExecutionSource
{
    Task<MonitoringObservation> ReadAsync(IntegrationMonitor rule, DateTime now, CancellationToken ct, DateTime? afterUtc = null);
}

internal static class MonitoringReplayPolicy
{
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    internal const int MaxExecutions = 4096;
    internal const int MaxIdentities = 8192;
}

internal sealed class MonitoringExecutionSource<TTimeTicker, TCronTicker>(ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
    : IMonitoringExecutionSource
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    internal const int PageSize = TickerMonitoringSnapshot.PageSize;

    private static MonitorExecution Map(TickerMonitoringExecution row)
        => new(row.Id, row.Status, row.CompletedUtc, row.ElapsedMilliseconds, row.Generation) { IsCron = row.IsCron };

    public async Task<MonitoringObservation> ReadAsync(IntegrationMonitor rule, DateTime now, CancellationToken ct, DateTime? afterUtc = null)
    {
        var since = afterUtc ?? now.AddHours(-Math.Max(72, rule.LookbackHours));
        var page = await provider.ReadMonitoringSnapshotAsync(rule.Function, since, now, ct).ConfigureAwait(false);
        if (page.Error != null) return new(now, null, [], [], page.Error);
        var recent = page.Completed.Select(Map)
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
        var replayed = new List<MonitorExecution>();
        if (afterUtc is { } checkpoint)
        {
            // Completion timestamps precede the commit that makes them visible. Re-read
            // a bounded overlap on every page, including catch-up pages; advancing a backlog
            // must not strand a late commit behind an intermediate checkpoint. The evaluator
            // durably deduplicates by execution generation.
            var cursor = checkpoint - MonitoringReplayPolicy.Window;
            while (cursor < checkpoint)
            {
                var overlap = await provider.ReadMonitoringSnapshotAsync(rule.Function, cursor, checkpoint, ct).ConfigureAwait(false);
                if (overlap.Error != null) return new(now, null, [], [], overlap.Error);
                var rows = overlap.Completed.OrderBy(x => x.CompletedUtc).ToArray();
                var hasMore = rows.Length > PageSize;
                var boundary = hasMore ? rows[PageSize].CompletedUtc!.Value.AddTicks(-1) : checkpoint;
                var included = rows.Where(x => x.CompletedUtc <= boundary).ToArray();
                if ((hasMore && included.Length == 0) || replayed.Count + included.Length > MonitoringReplayPolicy.MaxExecutions)
                    return new(now, null, [], [], "Completion replay exceeded its safe limit; monitoring cannot confirm health or advance its checkpoint.");
                replayed.AddRange(included.Select(Map));
                cursor = boundary;
            }
        }
        return new(now, success, recent, running.Select(x => new MonitorRunning(x.Id, x.Generation)).DistinctBy(x => x.Id).ToArray())
        { ThroughUtc = through, HasMore = more, Replayed = replayed.ToArray() };
    }
}
