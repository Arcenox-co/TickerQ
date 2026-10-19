using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Integrations;

/// <summary>Payload-free execution metadata used by integration monitors.</summary>
public sealed class TickerMonitoringExecution
{
    public Guid Id { get; init; }
    public bool IsCron { get; init; }
    public TickerStatus Status { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public Guid? Generation { get; init; }
}

/// <summary>
/// A bounded metadata page for one function in the provider's runtime partition.
/// Completed rows are ordered by completion time, in (afterUtc, throughUtc].
/// Includes one lookahead row; running rows are only needed on the final page.
/// Recurring-chain dispatchers are excluded; their time-ticker steps are included.
/// </summary>
public sealed class TickerMonitoringSnapshot
{
    public const int PageSize = 512;
    public const int MaxRunning = 1000;
    public const int MaxCronDefinitions = 200;

    public TickerMonitoringExecution[] Completed { get; init; } = [];
    public TickerMonitoringExecution[] Running { get; init; } = [];
    public string Error { get; init; }
}

/// <summary>Bounds retained metadata even when a provider must scan its store.</summary>
internal sealed class TickerMonitoringCollector(DateTime afterUtc, DateTime throughUtc)
{
    private readonly SortedSet<TickerMonitoringExecution> _completed = new(Comparer<TickerMonitoringExecution>.Create((a, b) =>
    {
        var comparison = Nullable.Compare(a.CompletedUtc, b.CompletedUtc);
        if (comparison == 0) comparison = a.IsCron.CompareTo(b.IsCron);
        return comparison == 0 ? a.Id.CompareTo(b.Id) : comparison;
    }));
    private readonly Dictionary<(bool, Guid), TickerMonitoringExecution> _completedIds = new();
    private readonly Dictionary<(bool, Guid), TickerMonitoringExecution> _running = new();

    internal void Add(TickerMonitoringExecution execution)
    {
        var key = (execution.IsCron, execution.Id);
        if (execution.Status == TickerStatus.InProgress)
        {
            if (_running.Count <= TickerMonitoringSnapshot.MaxRunning) _running.TryAdd(key, execution);
            return;
        }
        if (execution.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed or TickerStatus.Cancelled)
            || !(execution.CompletedUtc > afterUtc && execution.CompletedUtc <= throughUtc)
            || _completedIds.ContainsKey(key)) return;

        _completed.Add(execution);
        _completedIds.Add(key, execution);
        if (_completed.Count > TickerMonitoringSnapshot.PageSize + 1)
        {
            var last = _completed.Max!;
            _completed.Remove(last);
            _completedIds.Remove((last.IsCron, last.Id));
        }
    }

    internal TickerMonitoringSnapshot Build() => new()
    {
        Completed = _completed.ToArray(),
        Running = _completed.Count > TickerMonitoringSnapshot.PageSize ? [] : _running.Values.ToArray()
    };

    internal static TickerMonitoringSnapshot TooManyDefinitions() => new()
    {
        Error = $"More than {TickerMonitoringSnapshot.MaxCronDefinitions} cron definitions match this function."
    };
}
